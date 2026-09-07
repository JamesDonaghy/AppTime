using System;

namespace AppTime
{
    /// A single continuous stretch of time an app was observed running. Recorded
    /// separately from AppEntry.TotalUsageTime (a running lifetime total) so that
    /// per-day stats like "Sessions Today" can be based on real start/end data
    /// instead of just the all-time total.
    public class AppSession
    {
        // References AppEntry.Id rather than embedding the app itself, so a session
        // stays a small, simple record on its own.
        public Guid AppId { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime EndTime { get; set; }
    }
}