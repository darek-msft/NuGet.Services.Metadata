﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace NuGet.Services.Metadata.Catalog
{
    public class NuspecPackageCatalogItem : PackageCatalogItem
    {
        XDocument _nuspec;
        DateTime? _published;
        IEnumerable<PackageEntry> _entries;
        long? _packageSize;
        string _packageHash;
        private readonly IEnumerable<GraphAddon> _catalogSections;

        public NuspecPackageCatalogItem(string path)
        {
            Path = path;
            _published = null;
            _entries = null;
            _packageSize = null;
            _packageHash = null;
        }

        public NuspecPackageCatalogItem(XDocument nuspec, DateTime? published = null, IEnumerable<PackageEntry> entries = null, long? packageSize = null, string packageHash = null, IEnumerable<GraphAddon> catalogSections = null)
        {
            _nuspec = nuspec;
            _published = published;
            _entries = entries;
            _packageSize = packageSize;
            _packageHash = packageHash;
            _catalogSections = catalogSections;
        }

        public string Path
        {
            get;
            private set;
        }

        protected override XDocument GetNuspec()
        {
            if (_nuspec == null)
            {
                lock(this)
                {
                    if (_nuspec == null)
                    {
                        using (StreamReader reader = new StreamReader(Path))
                        {
                            return XDocument.Load(reader);
                        }
                    }
                }
            }

            return _nuspec;
        }

        protected override DateTime? GetPublished()
        {
            return _published;
        }

        protected override IEnumerable<PackageEntry> GetEntries()
        {
            return _entries;
        }

        protected override long? GetPackageSize()
        {
            return _packageSize;
        }

        protected override string GetPackageHash()
        {
            return _packageHash;
        }

        protected override IEnumerable<GraphAddon> GetAddons()
        {
            return _catalogSections ?? base.GetAddons();
        }
    }
}