﻿using System.Linq;
using System.Web;
using System.Web.Mvc;
using Lokad.Cloud.Console.WebRole.Behavior;
using Lokad.Cloud.Console.WebRole.Controllers.ObjectModel;
using Lokad.Cloud.Console.WebRole.Framework.Discovery;
using Lokad.Cloud.Console.WebRole.Models.Logs;
using Lokad.Cloud.Diagnostics;

namespace Lokad.Cloud.Console.WebRole.Controllers
{
    using System.Collections.Generic;

    [RequireAuthorization, RequireDiscovery]
    public sealed class LogsController : TenantController
    {
        private const int InitialEntriesCount = 15;
        private const int MoreEntriesCount = 50;

        public LogsController(AzureDiscoveryInfo discoveryInfo)
            : base(discoveryInfo)
        {
        }

        public override ActionResult ByHostedService(string hostedServiceName)
        {
            InitializeDeploymentTenant(hostedServiceName);

            var cloudLogger = new CloudLogger(Storage.BlobStorage, string.Empty);
            var entryList = cloudLogger.GetLogsOfLevelOrHigher(LogLevel.Info).Take(InitialEntriesCount).ToArray();

            return View(this.LogEntriesToModel(entryList, InitialEntriesCount));
        }

        public ActionResult JsonEntriesAfter(string hostedServiceName, int skip, string oldestToken, string threshold)
        {
            InitializeDeploymentTenant(hostedServiceName);

            var cloudLogger = new CloudLogger(Storage.BlobStorage, string.Empty);
            var entries = cloudLogger.GetLogsOfLevelOrHigher(EnumUtil.Parse<LogLevel>(threshold, true), skip);
            int requestedCount = InitialEntriesCount;
            if(!string.IsNullOrEmpty(oldestToken))
            {
                requestedCount = MoreEntriesCount;
                entries = entries.SkipWhile(entry => string.Compare(EntryToToken(entry), oldestToken) >= 0);
            }
            var entryList = entries.Take(requestedCount).ToArray();

            return Json(this.LogEntriesToModel(entryList, requestedCount), JsonRequestBehavior.AllowGet);
        }

        private LogsModel LogEntriesToModel(IList<LogEntry> entryList, int requestedCount)
        {
            return new LogsModel
            {
                NewestToken = entryList.Count > 0 ? EntryToToken(entryList[0]) : string.Empty,
                OldestToken = entryList.Count > 0 ? EntryToToken(entryList[entryList.Count - 1]) : string.Empty,
                MoreAvailable = entryList.Count == requestedCount,
                Entries = entryList.Select(entry => new LogItem
                {
                    Token = this.EntryToToken(entry),
                    Time = HttpUtility.HtmlEncode(entry.DateTimeUtc),
                    Level = HttpUtility.HtmlEncode(entry.Level),
                    Message = HttpUtility.HtmlEncode(entry.Message),
                    Error = HttpUtility.HtmlEncode(entry.Error ?? string.Empty)
                }).ToArray()
            };
        }

        string EntryToToken(LogEntry entry)
        {
            return entry.DateTimeUtc.ToString("yyyyMMddHHmmssffff");
        }
    }
}
