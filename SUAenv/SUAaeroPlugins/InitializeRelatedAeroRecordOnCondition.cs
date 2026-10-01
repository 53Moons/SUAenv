using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;

namespace SUAenvPlugins.Action
{
    public class InitializeRelatedAeroRecordsOnCondition : PluginBase
    {
        // Entity names
        private const string ParentEntity = "sua_action";
        private const string ChildEntityAeronautical = "sua_aeronautical";
        private const string ChildEntityBaseline = "sua_baseline";
        private const string ChildEntityAlert = "sua_alerts";
        private const string ChildEntityCFA = "sua_controlledfiringarea";

        // Date mapping names
        private const string AeroFormalProposalDateField = "sua_formalproposaldate";
        private const string ChildFormalProposalReceivedField = "sua_formalproposalreceived"; // Used for both CFA and Alerts

        // Lookup field names
        private const string BaselineChildLookup = "sua_baseline";
        private const string AlertsChildLookup = "sua_alerts";
        private const string ActionLookupField = "sua_action";
        private const string TypeOfActionField = "sua_typeofaction";
        private const string CFAChildLookup = "sua_controlledfiringarea";

        // Option set values for Type of Action
        private const int ActionTypeAlertArea = 5;
        private const int ActionTypeNSA = 6;
        private const int ActionTypeCFA = 7;

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
                        CFAChildLookup,
                        TypeOfActionField,
                        ActionLookupField,
                        AeroFormalProposalDateField // Read from Aeronautical using its specific field name
                    )
                );

                var typeOfActionOptionSet = currentAeroState.GetAttributeValue<OptionSetValue>(TypeOfActionField);
                bool isAlertOrNSA = false;
                bool isCFA = false;

                if (typeOfActionOptionSet != null)
                {
                    int typeValue = typeOfActionOptionSet.Value;
                    if (typeValue == ActionTypeAlertArea || typeValue == ActionTypeNSA)
                        isAlertOrNSA = true;
                    else if (typeValue == ActionTypeCFA)
                        isCFA = true;
                }

                EntityReference alertRef = currentAeroState.GetAttributeValue<EntityReference>(AlertsChildLookup);
                EntityReference baselineRef = currentAeroState.GetAttributeValue<EntityReference>(BaselineChildLookup);
                EntityReference cfaRef = currentAeroState.GetAttributeValue<EntityReference>(CFAChildLookup);

                // SAFELY GRAB THE ACTION ID
                EntityReference actionRef = null;
                if (targetEntity.Contains(ActionLookupField) && targetEntity[ActionLookupField] != null)
                    actionRef = targetEntity.GetAttributeValue<EntityReference>(ActionLookupField);
                else if (currentAeroState.Contains(ActionLookupField) && currentAeroState[ActionLookupField] != null)
                    actionRef = currentAeroState.GetAttributeValue<EntityReference>(ActionLookupField);

                // SAFELY GRAB FORMAL PROPOSAL DATE FROM AERONAUTICAL
                DateTime? formalProposalDate = null;
                if (targetEntity.Contains(AeroFormalProposalDateField) && targetEntity[AeroFormalProposalDateField] != null)
                    formalProposalDate = targetEntity.GetAttributeValue<DateTime>(AeroFormalProposalDateField);
                else if (currentAeroState.Contains(AeroFormalProposalDateField) && currentAeroState[AeroFormalProposalDateField] != null)
                    formalProposalDate = currentAeroState.GetAttributeValue<DateTime>(AeroFormalProposalDateField);


                if (isAlertOrNSA)
                {
                    // Action is Alert/NSA: Generate Baseline AND Alert record
                    Guid? createdBaselineId = null;

                    // 1. Ensure Baseline Exists
                    if (baselineRef == null)
                    {
                        Entity newBaseline = new Entity(ChildEntityBaseline);
                        if (actionRef != null) newBaseline["sua_action"] = new EntityReference(ParentEntity, actionRef.Id);

                        createdBaselineId = sysService.Create(newBaseline);
                        AeroFormUpdate[BaselineChildLookup] = new EntityReference(ChildEntityBaseline, createdBaselineId.Value);
                        needsUpdate = true;
                    }
                    else
                    {
                        createdBaselineId = baselineRef.Id;
                    }

                    // 2. Generate Alert Record
                    if (alertRef == null)
                    {
                        tracer.Trace("Action Type is Alert/NSA. Creating new sua_alerts record.");
                        Entity newAlert = new Entity(ChildEntityAlert);

                        // Pass the Aeronautical Lookup
                        newAlert["sua_aeronautical"] = new EntityReference(ChildEntityAeronautical, targetEntity.Id);

                        // Pass the Baseline Lookup
                        if (createdBaselineId.HasValue)
                            newAlert["sua_baseline"] = new EntityReference(ChildEntityBaseline, createdBaselineId.Value);

                        // MAP TO THE ALERT SPECIFIC FIELD NAME
                        if (formalProposalDate.HasValue)
                            newAlert[ChildFormalProposalReceivedField] = formalProposalDate.Value;

                        Guid newAlertId = sysService.Create(newAlert);
                        AeroFormUpdate[AlertsChildLookup] = new EntityReference(ChildEntityAlert, newAlertId);
                        needsUpdate = true;
                    }

                    // Clear CFA if it exists
                    if (cfaRef != null) { AeroFormUpdate[CFAChildLookup] = null; needsUpdate = true; }
                }
                else if (isCFA)
                {
                    // Action is CFA: Generate Baseline AND CFA record
                    Guid? createdBaselineId = null;

                    // 1. Ensure Baseline Exists
                    if (baselineRef == null)
                    {
                        Entity newBaseline = new Entity(ChildEntityBaseline);
                        if (actionRef != null) newBaseline["sua_action"] = new EntityReference(ParentEntity, actionRef.Id);

                        createdBaselineId = sysService.Create(newBaseline);
                        AeroFormUpdate[BaselineChildLookup] = new EntityReference(ChildEntityBaseline, createdBaselineId.Value);
                        needsUpdate = true;
                    }
                    else
                    {
                        createdBaselineId = baselineRef.Id;
                    }

                    // 2. Generate CFA Record
                    if (cfaRef == null)
                    {
                        tracer.Trace("Action Type is CFA. Creating new sua_controlledfiringarea record.");
                        Entity newCfa = new Entity(ChildEntityCFA);

                        // Pass the Aeronautical Lookup
                        newCfa["sua_aeronautical"] = new EntityReference(ChildEntityAeronautical, targetEntity.Id);

                        // Pass the Baseline Lookup
                        if (createdBaselineId.HasValue)
                            newCfa["sua_baseline"] = new EntityReference(ChildEntityBaseline, createdBaselineId.Value);

                        // MAP TO THE CFA SPECIFIC FIELD NAME
                        if (formalProposalDate.HasValue)
                            newCfa[ChildFormalProposalReceivedField] = formalProposalDate.Value;

                        Guid newCfaId = sysService.Create(newCfa);
                        AeroFormUpdate[CFAChildLookup] = new EntityReference(ChildEntityCFA, newCfaId);
                        needsUpdate = true;
                    }

                    // Clear Alert if it exists
                    if (alertRef != null) { AeroFormUpdate[AlertsChildLookup] = null; needsUpdate = true; }
                }
                else
                {
                    // Action is Standard: Generate single baseline and clear others
                    if (baselineRef == null)
                    {
                        Entity newBaseline = new Entity(ChildEntityBaseline);
                        if (actionRef != null) newBaseline["sua_action"] = new EntityReference(ParentEntity, actionRef.Id);

                        Guid newBaselineId = sysService.Create(newBaseline);
                        AeroFormUpdate[BaselineChildLookup] = new EntityReference(ChildEntityBaseline, newBaselineId);
                        needsUpdate = true;
                    }

                    if (alertRef != null) { AeroFormUpdate[AlertsChildLookup] = null; needsUpdate = true; }
                    if (cfaRef != null) { AeroFormUpdate[CFAChildLookup] = null; needsUpdate = true; }
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