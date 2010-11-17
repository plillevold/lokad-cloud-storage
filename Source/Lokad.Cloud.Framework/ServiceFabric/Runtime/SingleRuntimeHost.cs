﻿#region Copyright (c) Lokad 2009-2010
// This code is released under the terms of the new BSD licence.
// URL: http://www.lokad.com/
#endregion

using System;
using System.IO;
using System.Reflection;
using System.Security;
using System.Threading;
using Autofac.Builder;

namespace Lokad.Cloud.ServiceFabric.Runtime
{
	/// <summary>
	/// AppDomain-isolated host for a single runtime instance.
	/// </summary>
	internal class IsolatedSingleRuntimeHost
	{
		/// <summary>Refer to the callee instance (isolated). This property is not null
		/// only for the caller instance (non-isolated).</summary>
		volatile SingleRuntimeHost _isolatedInstance;

		/// <summary>
		/// Run the hosted runtime, blocking the calling thread.
		/// </summary>
		/// <returns>True if the worker stopped as planned (e.g. due to updated assemblies)</returns>
		public bool Run()
		{
			var settings = RoleConfigurationSettings.LoadFromRoleEnvironment();

			// The trick is to load this same assembly in another domain, then
			// instantiate this same class and invoke Run
			var domain = AppDomain.CreateDomain("WorkerDomain", null, AppDomain.CurrentDomain.SetupInformation);

			bool restartForAssemblyUpdate;

			try
			{
				_isolatedInstance = (SingleRuntimeHost)domain.CreateInstanceAndUnwrap(
					Assembly.GetExecutingAssembly().FullName,
					typeof(SingleRuntimeHost).FullName);

				// This never throws, unless something went wrong with IoC setup and that's fine
				// because it is not possible to execute the worker
				restartForAssemblyUpdate = _isolatedInstance.Run(settings);
			}
			finally
			{
				_isolatedInstance = null;

				// If this throws, it's because something went wrong when unloading the AppDomain
				// The exception correctly pulls down the entire worker process so that no AppDomains are
				// left in memory
				AppDomain.Unload(domain);
			}

			return restartForAssemblyUpdate;
		}

		/// <summary>
		/// Immediately stop the runtime host and wait until it has exited (or a timeout expired).
		/// </summary>
		public void Stop()
		{
			var instance = _isolatedInstance;
			if (null != instance)
			{
				_isolatedInstance.Stop();
			}
		}
	}

	/// <summary>
	/// Host for a single runtime instance.
	/// </summary>
	internal class SingleRuntimeHost : MarshalByRefObject, IDisposable
	{
		/// <summary>Current hosted runtime instance.</summary>
		volatile Runtime _runtime;

		/// <summary>
		/// Manual-reset wait handle, signaled once the host stopped running.
		/// </summary>
		readonly EventWaitHandle _stoppedWaitHandle = new ManualResetEvent(false);

		/// <summary>
		/// Run the hosted runtime, blocking the calling thread.
		/// </summary>
		/// <returns>True if the worker stopped as planned (e.g. due to updated assemblies)</returns>
		public bool Run(Maybe<ICloudConfigurationSettings> externalRoleConfiguration)
		{
			_stoppedWaitHandle.Reset();

			// IoC Setup

			var builder = new ContainerBuilder();
			builder.RegisterModule(new CloudModule());
			if (externalRoleConfiguration.HasValue)
			{
				builder.RegisterModule(new CloudConfigurationModule(externalRoleConfiguration.Value));
			}
			else
			{
				builder.RegisterModule(new CloudConfigurationModule());
			}

			builder.Register(typeof (Runtime)).FactoryScoped();

			// Run

			using (var container = builder.Build())
			{
				var log = container.Resolve<ILog>();

				_runtime = null;
				try
				{
					_runtime = container.Resolve<Runtime>();
					_runtime.RuntimeContainer = container;

					// runtime endlessly keeps pinging queues for pending work
					_runtime.Execute();

					log.Log(LogLevel.Debug, string.Format(
						"Runtime Host: Runtime has stopped cleanly on worker {0}.",
						CloudEnvironment.PartitionKey));
				}
				catch (TypeLoadException typeLoadException)
				{
					log.Log(LogLevel.Error, typeLoadException, string.Format(
						"Runtime Host: Type {0} could not be loaded. The Runtime Host will be restarted.",
						typeLoadException.TypeName));
				}
				catch (FileLoadException fileLoadException)
				{
					// Tentatively: referenced assembly is missing
					log.Log(LogLevel.Error, fileLoadException, string.Format(
						"Runtime Host: Could not load assembly probably due to a missing reference assembly. The Runtime Host will be restarted."));
				}
				catch (SecurityException securityException)
				{
					// Tentatively: assembly cannot be loaded due to security config
					log.Log(LogLevel.Error, securityException, string.Format(
						"Runtime Host: Could not load assembly {0} probably due to security configuration. The Runtime Host will be restarted.",
						securityException.FailedAssemblyInfo));
				}
				catch (TriggerRestartException)
				{
					log.Log(LogLevel.Debug, string.Format(
						"Runtime Host: Triggered to stop execution on worker {0}. The Role Instance will be recycled and the Runtime Host restarted.",
						CloudEnvironment.PartitionKey));

					return true;
				}
				catch (Exception ex)
				{
					// Generic exception
					log.Log(LogLevel.Error, ex, string.Format(
						"Runtime Host: An unhandled exception occurred on worker {0}. The Runtime Host will be restarted.",
						CloudEnvironment.PartitionKey));
				}
				finally
				{
					_stoppedWaitHandle.Set();
					_runtime = null;
				}

				return false;
			}
		}

		/// <summary>
		/// Immediately stop the runtime host and wait until it has exited (or a timeout expired).
		/// </summary>
		public void Stop()
		{
			var runtime = _runtime;
			if (null != runtime)
			{
				runtime.Stop();

				// note: we DO have to wait until the shut down has finished,
				// or the Azure Fabric will tear us apart early!
				_stoppedWaitHandle.WaitOne(25.Seconds());
			}
		}

		public void Dispose()
		{
			_stoppedWaitHandle.Close();
		}
	}
}
