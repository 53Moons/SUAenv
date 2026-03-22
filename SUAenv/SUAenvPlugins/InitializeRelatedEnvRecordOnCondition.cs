using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;

namespace SUAenvPlugins.Action
{
    public class InitializeRelatedEnvRecordOnCondition : PluginBase
    {
        // Entity References
        private const string ParentEntity = "sua_environmental";
        private const string ChildEntityEA = "sua_environmentalassessment";
        private const string ChildEntityEIS = "sua_eis";
        private const string ChildEntityCATEX = "sua_environmentalcatex";

        // Lookup Fields
        private const string ParentLookup = "sua_environmental";
        private const string EAChildLookup = "sua_ea";
        private const string EISChildLookup = "sua_eis";
        private const string CATEXChildLookup = "sua_catex";

        // Env Action Type OptionSet Values
        private const int CATEX = 0;
        private const int EA = 1;
        private const int EIS = 2;
        private const string NepaAction = "sua_nepaaction";
    
        public InitializeRelatedEnvRecordOnCondition()
            : base(typeof(InitializeRelatedEnvRecordOnCondition))
        {
            // not implemented
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
                {
                    throw new InvalidPluginExecutionException("Invalid execution context");
                }

                Entity targetEntity = (Entity)context.InputParameters["Target"];

                // Get parent or exit
                if (targetEntity.LogicalName != ParentEntity) return;

                // Get nepa action optionset exit if null
                var nepaActionOptionSet = targetEntity.GetAttributeValue<OptionSetValue>(NepaAction);
                if (nepaActionOptionSet == null) return;

                int nepaActionValue = nepaActionOptionSet.Value;

                // Associate nepa action type with form so we can switch
                // Updated to fix recursive pattern error - case labels const value, break to skip if not needed
                string targetChildEntityName = string.Empty;
                string parentLookupToUpdate = string.Empty;

                switch (nepaActionValue)
                {
                  case EA: targetChildEntityName =ChildEntityEA;
                           parentLookupToUpdate = EAChildLookup;
                           break;
                  
                    case EIS: targetChildEntityName = ChildEntityEIS; 
                              parentLookupToUpdate = EISChildLookup;
                              break;

                  case CATEX: targetChildEntityName = ChildEntityCATEX; 
                              parentLookupToUpdate = CATEXChildLookup;
                              break;                   
                }

                // Lookups clear before update
                Entity parentToUpdate = new Entity(ParentEntity, targetEntity.Id);
                parentToUpdate[EAChildLookup] = null;
                parentToUpdate[EISChildLookup] = null;
                parentToUpdate[CATEXChildLookup] = null;

                // Exit if its none of these (like NA)
                if (string.IsNullOrEmpty(targetChildEntityName))
                {
                    sysService.Update(parentToUpdate);
                    return;
                }

                // Does a child record already exist
                QueryExpression query = new QueryExpression(targetChildEntityName)
                {
                    ColumnSet = new ColumnSet(false),
                    TopCount = 1
                };

                query.Criteria.AddCondition(ParentLookup, ConditionOperator.Equal, targetEntity.Id);
                EntityCollection results =
                    sysService.RetrieveMultiple(query);

                Guid childRecordId = Guid.Empty;

                // Create child record if needed else exit
                if (results.Entities.Count == 0)
                {
                    tracer.Trace($"No {targetChildEntityName} record exists. Create new record");

                    Entity newChildRecord = new Entity(targetChildEntityName);
                    newChildRecord[ParentLookup] = new EntityReference(ParentEntity, targetEntity.Id);

                    // Get new child id
                    childRecordId = sysService.Create(newChildRecord);
                }
                else
                {
                    // Get existing child id
                 childRecordId = results.Entities[0].Id;             

                }

                // Update child lookup on parent
                if (childRecordId != Guid.Empty && !string.IsNullOrEmpty(parentLookupToUpdate))
                {                   
                    parentToUpdate[parentLookupToUpdate] = new EntityReference(targetChildEntityName, childRecordId);
                    sysService.Update(parentToUpdate);
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