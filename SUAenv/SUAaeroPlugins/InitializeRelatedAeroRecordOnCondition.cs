using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;

namespace SUAenvPlugins.Action
{
    public class InitializeRelatedAeroRecordsOnCondition : PluginBase
    {
        // Entity References       
        private const string ChildEntityAeronautical = "sua_aeronautical";
        private const string ChildEntityBaseline = "sua_baseline";
        private const string ChildEntitySupplemental = "sua_supplementalrulemaking";
        private const string ChildEntityAlert = "sua_alerts";

        // Lookup Fields       
        private const string BaselineChildLookup = "sua_baseline";
        private const string Baseline2ChildLookup = "sua_baseline2";
        private const string SupplementalRulemakingChildLookup = "sua_supplementalrulemaking";
        private const string AlertsChildLookup = "sua_alerts";

        // OptionSet / Field Names
        private const string TypeOfActionField = "sua_typeofaction";
        private const string RequiresSupplementalField = "sua_requiressupplementalrulemaking";

        // OptionSet Values
        private const int ActionTypeAlertArea = 5;
        private const int ActionTypeNSA = 6;

        public InitializeRelatedAeroRecordsOnCondition()
            : base(typeof(InitializeRelatedAeroRecordsOnCondition))
        {
        }

        protected override void ExecuteCdsPlugin(ILocalPluginContext localPluginContext)
        {
            var context = localPluginContext.PluginExecutionContext;
            var sysService = localPluginContext.SystemUserService;
            var tracer = localPluginContext.TracingService;

            if (context.Depth > 1) return;
            if ((context.MessageName != "Create" && context.MessageName != "Update") || context.Stage != 40)
                return;

            try
            {
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity))
                    throw new InvalidPluginExecutionException("Invalid execution context");

                Entity targetEntity = (Entity)context.InputParameters["Target"];

                // Get aeronautical parent record or exit
                if (targetEntity.LogicalName != ChildEntityAeronautical) return;

                Entity AeroFormUpdate = new Entity(ChildEntityAeronautical, targetEntity.Id);
                bool needsUpdate = false;

                // Fetch the current state of lookups and fields
                Entity currentAeroState = sysService.Retrieve(
                    ChildEntityAeronautical,
                    targetEntity.Id,
                    new ColumnSet(
                        BaselineChildLookup,
                        Baseline2ChildLookup,
                        SupplementalRulemakingChildLookup,
                        AlertsChildLookup,
                        TypeOfActionField,
                        RequiresSupplementalField
                    )
                );

                // Look at the type of action
                var typeOfActionOptionSet = currentAeroState.GetAttributeValue<OptionSetValue>(TypeOfActionField);
                bool isAlertOrNSA = false;

                if (typeOfActionOptionSet != null)
                {
                    int typeValue = typeOfActionOptionSet.Value;
                    if (typeValue == ActionTypeAlertArea || typeValue == ActionTypeNSA)
                    {
                        isAlertOrNSA = true;
                    }
                }

                EntityReference alertRef = currentAeroState.GetAttributeValue<EntityReference>(AlertsChildLookup);
                EntityReference baselineRef = currentAeroState.GetAttributeValue<EntityReference>(BaselineChildLookup);
                EntityReference baseline2Ref = currentAeroState.GetAttributeValue<EntityReference>(Baseline2ChildLookup);

                if (isAlertOrNSA)
                {
                    // Action is Alert/NSA: Generate Alerts record, clear Baselines
                    if (alertRef == null)
                    {
                        tracer.Trace("Action Type is Alert/NSA. Creating new sua_alerts record.");
                        Guid newAlertId = sysService.Create(new Entity(ChildEntityAlert));
                        AeroFormUpdate[AlertsChildLookup] = new EntityReference(ChildEntityAlert, newAlertId);
                        needsUpdate = true;
                    }

                    if (baselineRef != null)
                    {
                        AeroFormUpdate[BaselineChildLookup] = null;
                        needsUpdate = true;
                    }
                    if (baseline2Ref != null)
                    {
                        AeroFormUpdate[Baseline2ChildLookup] = null;
                        needsUpdate = true;
                    }
                }
                else
                {
                    // Generate baseline records if conditions met and clear alerts
                    if (baselineRef == null)
                    {
                        tracer.Trace("Creating Primary sua_baseline record.");
                        Guid newBaselineId = sysService.Create(new Entity(ChildEntityBaseline));
                        AeroFormUpdate[BaselineChildLookup] = new EntityReference(ChildEntityBaseline, newBaselineId);
                        needsUpdate = true;
                    }

                    if (baseline2Ref == null)
                    {
                        tracer.Trace("Creating Secondary sua_baseline record (baseline2).");
                        Guid newBaseline2Id = sysService.Create(new Entity(ChildEntityBaseline));
                        AeroFormUpdate[Baseline2ChildLookup] = new EntityReference(ChildEntityBaseline, newBaseline2Id);
                        needsUpdate = true;
                    }

                    if (alertRef != null)
                    {
                        AeroFormUpdate[AlertsChildLookup] = null;
                        needsUpdate = true;
                    }
                }

                // Check supplemental rulemaking requirement (Yes/No Boolean)
                bool reqSupplemental = currentAeroState.GetAttributeValue<bool>(RequiresSupplementalField);
                EntityReference suppRef = currentAeroState.GetAttributeValue<EntityReference>(SupplementalRulemakingChildLookup);

                if (reqSupplemental && suppRef == null)
                {
                    tracer.Trace("Requires Supplemental Rulemaking is True. Creating sua_supplementalrulemaking record.");
                    Guid suppId = sysService.Create(new Entity(ChildEntitySupplemental));
                    AeroFormUpdate[SupplementalRulemakingChildLookup] = new EntityReference(ChildEntitySupplemental, suppId);
                    needsUpdate = true;
                }
                else if (!reqSupplemental && suppRef != null)
                {
                    tracer.Trace("Requires Supplemental Rulemaking is False. Clearing lookup.");
                    AeroFormUpdate[SupplementalRulemakingChildLookup] = null;
                    needsUpdate = true;
                }

                // Save changes if any updates were made
                if (needsUpdate)
                {
                    sysService.Update(AeroFormUpdate);
                }
            }
            catch (Exception ex)
            {
                tracer.Trace($"Unhandled exception: {ex.Message}");
                throw new InvalidPluginExecutionException(ex.Message, ex);
            }
        }
    }
}