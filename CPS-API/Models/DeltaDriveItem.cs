﻿using Microsoft.Graph.Models;

namespace CPS_API.Models
{
    public class DeltaDriveItem
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string DriveId { get; set; } = string.Empty;

        public Folder Folder { get; set; } = new();

        public Deleted Deleted { get; set; } = new();

        public DateTime? CreatedDateTime { get; set; }

        public DateTime? LastModifiedDateTime { get; set; }
    }
}