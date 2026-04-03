using RosMessageTypes.BuiltinInterfaces;
using RobotSNAP.Core;

namespace RobotSNAP.ROS
{
    /// <summary>
    /// ROS-specific time utilities.
    /// Contains conversion methods between Unity time and ROS messages.
    /// </summary>
    public static class ROSTimeUtils
    {
        /// <summary>
        /// Convert milliseconds to ROS TimeMsg
        /// </summary>
        public static TimeMsg MillisecondsToTimeMsg(double milliseconds)
        {
            int secs = (int)(milliseconds / 1000);
            uint nanosec = (uint)((milliseconds % 1000) * 1_000_000);
            return new TimeMsg(secs, nanosec);
        }
        
        /// <summary>
        /// Convert ROS TimeMsg to milliseconds
        /// </summary>
        public static double TimeMsgToMilliseconds(TimeMsg time)
        {
            return time.sec * 1000.0 + time.nanosec / 1_000_000.0;
        }
        
        /// <summary>
        /// Get the current time as a ROS TimeMsg
        /// </summary>
        public static TimeMsg Now()
        {
            return MillisecondsToTimeMsg(Clock.Instance.CurrentTimeMillis);
        }
        
        /// <summary>
        /// Get the last published time as a ROS TimeMsg
        /// </summary>
        public static TimeMsg GetLastPublishedTime()
        {
            return MillisecondsToTimeMsg(Clock.Instance.CurrentTimeMillis);
        }
        
        /// <summary>
        /// Update a ROS header with the current time
        /// </summary>
        public static void UpdateHeader(RosMessageTypes.Std.HeaderMsg header)
        {
            if (header == null) return;
            
            var timeMsg = Now();
            header.stamp.sec = timeMsg.sec;
            header.stamp.nanosec = timeMsg.nanosec;
        }
        
        /// <summary>
        /// Update multiple ROS headers with the current time
        /// </summary>
        public static void UpdateHeaders(RosMessageTypes.Std.HeaderMsg[] headers)
        {
            var timeMsg = Now();
            foreach (var header in headers)
            {
                if (header != null)
                {
                    header.stamp.sec = timeMsg.sec;
                    header.stamp.nanosec = timeMsg.nanosec;
                }
            }
        }
    }
}