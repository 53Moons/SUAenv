using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;

namespace SUAenvPlugins.Action
{
    public class InitializeRelatedAeroRecordsOnCondition : PluginBase
    {
        private const string ParentEntity = "sua_action";
        private const string ChildEntityAeronautical = "sua_aeronautical";
        private const string ChildEntityBaseline = "sua_baseline";
        private const string ChildEntityAlert = "sua_alerts";

        private const string BaselineChildLookup = "sua_baseline";
        private const string AlertsChildLookup = "sua_alerts";
        private const string ActionLookupField = "sua_action";
        private const string TypeOfActionField = "sua_typeofaction";

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

                if (targetEntity.LogicalName != ChildEntityAeronautical) return;

                Entity AeroFormUpdate = new Entity(ChildEntityAeronautical, targetEntity.Id);
                bool needsUpdate = false;

                Entity currentAeroState = sysService.Retrieve(
                    ChildEntityAeronautical,
                    targetEntity.Id,
                    new ColumnSet(
                        BaselineChildLookup,
                        AlertsChildLookup,
                        TypeOfActionField,
                        ActionLookupField
                    )
                );

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

                // SAFELY GRAB THE ACTION ID (Check Target first, then Database)
                EntityReference actionRef = null;
                if (targetEntity.Contains(ActionLookupField) && targetEntity[ActionLookupField] != null)
                {
                    actionRef = targetEntity.GetAttributeValue<EntityReference>(ActionLookupField);
                    tracer.Trace("Found Action ID in the Target data.");
                }
                else if (currentAeroState.Contains(ActionLookupField) && currentAeroState[ActionLookupField] != null)
                {
                    actionRef = currentAeroState.GetAttributeValue<EntityReference>(ActionLookupField);
                    tracer.Trace("Found Action ID in the Database.");
                }
                else
                {
                    tracer.Trace("WARNING: Action ID is null! Could not find it on the Aeronautical record.");
                }

                if (isAlertOrNSA)
                {
                    // Action is Alert/NSA: Generate alert record and clear single baseline
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
                }
                else
                {
                    // Action is NOT Alert/NSA: Generate single baseline and clear alerts
                    if (baselineRef == null)
                    {
                        tracer.Trace("Creating Primary sua_baseline record.");
                        Entity newBaseline = new Entity(ChildEntityBaseline);

                        if (actionRef != null)
                        {
                            // This maps the Parent Action to the new Baseline automatically
                            newBaseline["sua_action"] = new EntityReference(ParentEntity, actionRef.Id);
                            tracer.Trace($"Successfully mapped Action {actionRef.Id} to the new Baseline.");
                        }

                        Guid newBaselineId = sysService.Create(newBaseline);
                        AeroFormUpdate[BaselineChildLookup] = new EntityReference(ChildEntityBaseline, newBaselineId);
                        needsUpdate = true;
                    }

                    if (alertRef != null)
                    {
                        AeroFormUpdate[AlertsChildLookup] = null;
                        needsUpdate = true;
                    }
                }

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