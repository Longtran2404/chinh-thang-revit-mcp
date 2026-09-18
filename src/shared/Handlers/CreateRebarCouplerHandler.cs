// Chinh Thang: real Revit couplers, no cylindrical proxy geometry.
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin.Handlers
{
    public class CreateRebarCouplerHandler : IRevitCommand
    {
        public string Name => "create_rebar_coupler";
        public string Description => "Connect two explicit Rebar ends using a loaded native Rebar Coupler family type.";
        public string ParametersSchema => @"{""type"":""object"",""properties"":{""type_id"":{""type"":""integer""},""first_rebar_id"":{""type"":""integer""},""first_end"":{""type"":""integer""},""second_rebar_id"":{""type"":""integer""},""second_end"":{""type"":""integer""},""dry_run"":{""type"":""boolean""}},""required"":[""type_id"",""first_rebar_id"",""first_end"",""second_rebar_id"",""second_end""]}";
        public CommandResult Execute(UIApplication app,string json)
        {
            var doc=app.ActiveUIDocument?.Document;
            if(doc==null) return CommandResult.Fail("No document is open.");
            try { return Create(doc,JObject.Parse(json)); }
            catch(Exception ex) { return CommandResult.Fail("Coupler: "+ex.Message); }
        }
        public static CommandResult Create(Document doc,JObject p)
        {
            var first=doc.GetElement(RevitCompat.ToElementId(p.Value<long>("first_rebar_id"))) as Rebar;
            var second=doc.GetElement(RevitCompat.ToElementId(p.Value<long>("second_rebar_id"))) as Rebar;
            var type=doc.GetElement(RevitCompat.ToElementId(p.Value<long>("type_id"))) as FamilySymbol;
            int a=p.Value<int>("first_end"),b=p.Value<int>("second_end");
            if(first==null || second==null || first.Id==second.Id || a<0 || a>1 || b<0 || b>1) throw new ArgumentException("Two different native rebars and explicit end indexes 0/1 are required.");
            if(type==null || type.Category.Id!=new ElementId(BuiltInCategory.OST_Coupler)) throw new ArgumentException("Load a Rebar Coupler family and select its type; no proxy is generated.");
            bool dry=p.Value<bool?>("dry_run")??false;
            using(var group=new TransactionGroup(doc,"Chinh Thang: Coupler"))
            {
                group.Start();
                using(var tx=new Transaction(doc,"Chinh Thang: Coupler"))
                {
                    tx.Start();
                    tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new RebarPathFailures()).SetClearAfterRollback(true));
                    if(!type.IsActive) { type.Activate();doc.Regenerate(); }
                    RebarCouplerError error;
                    var coupler=RebarCoupler.Create(doc,type.Id,RebarReinforcementData.Create(first.Id,a),RebarReinforcementData.Create(second.Id,b),out error);
                    if(coupler==null || error!=RebarCouplerError.ValidationSuccessfuly) throw new InvalidOperationException(error.ToString());
                    doc.Regenerate();
                    var result=new {dry_run=dry,created_id=dry ? (long?)null : RevitCompat.GetId(coupler.Id),native_type=coupler.GetType().FullName,quantity=coupler.GetCouplerQuantity(),first_rebar_id=RevitCompat.GetId(first.Id),second_rebar_id=RevitCompat.GetId(second.Id),design_note="Native API compatibility passed; manufacturer certification, cover and mechanical splice design remain project checks."};
                    if(tx.Commit()!=TransactionStatus.Committed) return CommandResult.Fail("Revit rejected the coupler transaction.");
                    if(dry) group.RollBack(); else group.Assimilate();
                    return CommandResult.Ok(result);
                }
            }
        }
    }
}
