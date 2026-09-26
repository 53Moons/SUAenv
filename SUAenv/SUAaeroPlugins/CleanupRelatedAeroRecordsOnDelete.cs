using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;

namespace SUAaeroPlugins.Aeronautical
{
    public class CleanupRelatedAeroRecordsOnDelete : IPlugin
    {
        private const string TargetEntity = "sua_aeronautical";

        // Lookup Fields on sua_aeronautical
        private const string BaselineChildLookup = "sua_baseline";
        private const string Baseline2ChildLookup = "sua_baseline2";
        private const string SupplementalRulemakingChildLookup = "sua_supplementalrulemaking";
        private const string AlertsChildLookup = "sua_alerts";

        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracer = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService sysService = factory.CreateOrganizationService(context.UserId);

            // Must run on PreOperation so the record still exists to read the lookups!
            if (context.Depth > 1) return;
            if (context.MessageName != "Delete" || context.Stage != 20) return;

            try
            {
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is EntityReference))
                    return;

                EntityReference targetRef = (EntityReference)context.InputParameters["Target"];

                if (targetRef.LogicalName != TargetEntity) return;

                // Get aero record id
                Entity aeroToDelete = sysService.Retrieve(
                    TargetEntity,
                    targetRef.Id,
                    new ColumnSet(BaselineChildLookup, Baseline2ChildLookup, SupplementalRulemakingChildLookup, AlertsChildLookup)
                );

                // Get lookup references for related records
                EntityReference baselineRef = aeroToDelete.GetAttributeValue<EntityReference>(BaselineChildLookup);
                EntityReference baseline2Ref = aeroToDelete.GetAttributeValue<EntityReference>(Baseline2ChildLookup);
                EntityReference suppRef = aeroToDelete.GetAttributeValue<EntityReference>(SupplementalRulemakingChildLookup);
                EntityReference alertRef = aeroToDelete.GetAttributeValue<EntityReference>(AlertsChildLookup);

                // Delete existing related records if they exist
                if (baselineRef != null)
                {
                    tracer.Trace("Deleting orphaned Primary Baseline.");
                    sysService.Delete(baselineRef.LogicalName, baselineRef.Id);
                }

                if (baseline2Ref != null)
                {
                    tracer.Trace("Deleting orphaned Secondary Baseline.");
                    sysService.Delete(baseline2Ref.LogicalName, baseline2Ref.Id);
                }

                if (suppRef != null)
                {
                    tracer.Trace("Deleting orphaned Supplemental Rulemaking.");
                    sysService.Delete(suppRef.LogicalName, suppRef.Id);
                }

                if (alertRef != null)
                {
                    tracer.Trace("Deleting orphaned Alerts.");
                    sysService.Delete(alertRef.LogicalName, alertRef.Id);
                }
            }
            catch (Exception ex)
            {
                tracer.Trace($"Unhandled exception during cleanup: {ex.Message}");
                throw new InvalidPluginExecutionException(ex.Message, ex);
            }
        }
    }
}