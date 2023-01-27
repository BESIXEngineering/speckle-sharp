using Speckle.Core.Kits;
using Speckle.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Objects.Structural.SCIA.Loading
{
    public class SCIALoadGroup : Base
    {
        /* From https://www.saf.guide/en/stable/loads/structuralloadgroup.html:
         * Load groups define “how the individual load cases may be combined together” if inserted into a load case combination.
         * Thanks to the load groups, the user can easily specify which load cases MUST, MUST NOT, or CAN act together. 
         * Each load group may be used either for permanent loads or for variable loads. Permanent and variable loads cannot appear in the same group.
         */

        public string name { get; set; } //load group name
        public SCIALoadGroupType loadGroupType { get; set; }
        public SCIAVariableLoadType variableLoadType { get; set; }
        public SCIALoadGroupRelation relation { get; set; }
        
        public SCIALoadGroup() { }

        [SchemaInfo("SCIALoadGroup", "Creates a Speckle structural load group for SCIA", "SCIA", "Loading")]
        public SCIALoadGroup(string name, 
            [SchemaParamInfo("This parameters tell whether the load group is used for permanent or variable loads.\nApplicable Load group types for:\nPermanent load case: Permanent\nVariable load case: Variable, Seismic, Moving, Tensioning, Fire\nAccidental load case: Accidental")] SCIALoadGroupType loadGroupType, 
            [SchemaParamInfo("The relation tells what the relation of load cases in the particular load group is.")] SCIALoadGroupRelation relation = SCIALoadGroupRelation.NotDefined, 
            [SchemaParamInfo("Define type of variable load")] SCIAVariableLoadType variableLoadType = SCIAVariableLoadType.NotDefined)
        {
            this.name = name;
            this.loadGroupType = loadGroupType;
            this.variableLoadType = loadGroupType == SCIALoadGroupType.Variable ? variableLoadType : SCIAVariableLoadType.NotDefined;
            this.relation = relation;

            if (loadGroupType == SCIALoadGroupType.Variable && variableLoadType == null)
                throw new ArgumentNullException("Load type must be defined for Variable Load Groups");
        }
    }
}
