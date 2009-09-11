﻿#region Copyright (c) Lokad 2009
// This code is released under the terms of the new BSD licence.
// URL: http://www.lokad.com/
#endregion

using NUnit.Framework;

namespace Lokad.Cloud.Azure.Test
{
	[TestFixture]
	public class TypeMapperProviderTests
	{
		[Test]
		public void GetStorageName()
		{
			Assert.AreEqual(
				"lokad-cloud-azure-test-typemapperprovidertests", 
				TypeMapper.GetStorageName(typeof(TypeMapperProviderTests)));
		}
	}
}
