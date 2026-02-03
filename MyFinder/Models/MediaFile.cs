using System;
using System.Collections.Generic;

namespace MyFinder.Models;

public class MediaFile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public long FileSizeBytes { get; set; }
    
    // Low Content / AI Analysis
    public bool IsAnalyzed { get; set; } // True if analysis has run
    public bool IsLowContent { get; set; }
    public double ContentScore { get; set; } // 0.0 (empty) to 1.0 (rich)
    
    // Deduplication
    public string? HashMd5 { get; set; }
    public ulong? PerceptualHash { get; set; }
    
    // Metadata
    public DateTime? ExtractedTimestamp { get; set; }
    public List<string> PersonNames { get; set; } = new();
    public string? TranscriptSnippet { get; set; } // Short preview
    
    // New Features
    public List<string> Tags { get; set; } = new();
    public DateTime? LastOpened { get; set; }

    // Path to separate file containing full embeddings/transcript if needed
    public string? ExternalDataPath { get; set; }
}
