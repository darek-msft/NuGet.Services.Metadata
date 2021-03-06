﻿using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.Services.Metadata.Catalog
{
    public class PackageEntry
    {
        public PackageEntry()
        {
        }

        public PackageEntry(ZipArchiveEntry zipArchiveEntry)
        {
            FullName = zipArchiveEntry.FullName;
            Name = zipArchiveEntry.Name;
            Length = zipArchiveEntry.Length;
            CompressedLength = zipArchiveEntry.CompressedLength;
        }

        public string FullName { get; set; }
        public string Name { get; set; }
        public long Length { get; set; }
        public long CompressedLength { get; set; }
    }
}
