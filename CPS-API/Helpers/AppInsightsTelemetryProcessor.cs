using OpenTelemetry;
using System.Diagnostics;

namespace CPS_API.Helpers
{
    public class AppInsightsTelemetryProcessor : BaseProcessor<Activity>
    {
        private readonly ILogger<AppInsightsTelemetryProcessor> _logger;

        public AppInsightsTelemetryProcessor(ILogger<AppInsightsTelemetryProcessor> logger)
        {
            _logger = logger;
        }


        public override void OnStart(Activity activity)
        {
            _logger.LogInformation("Activity started with ID: {ActivityId}", activity.Id);

            // Generate and add CorrelationId if not present
            if (string.IsNullOrEmpty(activity.ParentId))
            {
                string correlationId = Guid.NewGuid().ToString();
                activity.SetTag("CorrelationId", correlationId);
                activity.SetParentId(correlationId);
            }
        }

        public override void OnEnd(Activity activity)
        {
            _logger.LogInformation("Activity ended with ID: {ActivityId}", activity.Id);
        }
    }
}
