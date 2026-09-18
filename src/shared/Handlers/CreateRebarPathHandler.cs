// Chinh Thang Revit MCP: native bent rebar, stair paths and support chairs.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin.Handlers
{
    public class CreateRebarPathHandler : IRevitCommand
    {
        public string Name => "create_rebar_path";
        public string Description => "Create native bent Rebar from explicit centreline vertices; no inferred anchorage or cover.";
        public string ParametersSchema => @"{""type"":""object"",""properties"":{""host_id"":{""type"":""integer""},""bar_type_id"":{""type"":""integer""},""points_json"":{""type"":""string""},""mode"":{""type"":""string""},""normal_x"":{""type"":""number""},""normal_y"":{""type"":""number""},""normal_z"":{""type"":""number""},""layout_rule"":{""type"":""string""},""quantity"":{""type"":""integer""},""distribution_length_mm"":{""type"":""number""},""spacing_mm"":{""type"":""number""},""dry_run"":{""type"":""boolean""}},""required"":[""host_id"",""bar_type_id"",""points_json"",""normal_x"",""normal_y"",""normal_z""]}";

        public CommandResult Execute(UIApplication app, string paramsJson)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return CommandResult.Fail("No document is open.");
            try { return Create(doc, JObject.Parse(paramsJson ?? "{}")); }
            catch (Exception ex) { return CommandResult.Fail("Rebar path: " + ex.Message); }
        }

        public static CommandResult Create(Document doc, JObject req)
        {
            var points = RebarPathInput.Points(req.Value<string>("points_json")).Select(p => new XYZ(p[0]/304.8,p[1]/304.8,p[2]/304.8)).ToList();
            var mode = req.Value<string>("mode") ?? "ShapeDriven";
            var rule = req.Value<string>("layout_rule") ?? "Single";
            var qty = req.Value<int?>("quantity") ?? 1;
            var length = req.Value<double?>("distribution_length_mm") ?? 0;
            var spacing = req.Value<double?>("spacing_mm") ?? 0;
            RebarPathInput.Layout(mode,rule,qty,length,spacing);
            var normal = new XYZ(req.Value<double>("normal_x"),req.Value<double>("normal_y"),req.Value<double>("normal_z"));
            if (double.IsNaN(normal.GetLength()) || double.IsInfinity(normal.GetLength()) || normal.GetLength()<1e-9) throw new ArgumentException("Provide a finite nonzero plane normal / FreeForm distribution direction.");
            normal = normal.Normalize();
            if (mode == "ShapeDriven" && points.Any(p => Math.Abs((p-points[0]).DotProduct(normal)) > 0.01/304.8))
                throw new ArgumentException("Path is not planar perpendicular to the normal. Use explicit FreeForm for spatial chairs.");
            var host = doc.GetElement(RevitCompat.ToElementId(req.Value<long>("host_id")));
            if (host == null || !RebarHostData.IsValidHost(host)) throw new ArgumentException("Explicit host must support rebar and have a structural concrete material.");
            var barType = doc.GetElement(RevitCompat.ToElementId(req.Value<long>("bar_type_id"))) as RebarBarType;
            if (barType == null) throw new ArgumentException("Explicit valid bar_type_id is required; no default diameter is selected.");
            var dry = req.Value<bool?>("dry_run") ?? false;
            using (var group = new TransactionGroup(doc, "Chinh Thang: Bent rebar path"))
            {
            group.Start();
            using (var tx = new Transaction(doc, "Chinh Thang: Bent rebar path"))
            {
                tx.Start();
                // Never leave a modal failure dialog or a partially committed set.
                    var failures=new RebarPathFailures();
                    tx.SetFailureHandlingOptions(tx.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                Rebar bar;
                if (mode == "FreeForm")
                {
                    var count = rule == "Single" ? 1 : rule == "FixedNumber" ? qty : (int)Math.Ceiling(length/spacing)+1;
                    var loops = new List<CurveLoop>();
                    for (int i=0;i<count;i++)
                        loops.Add(CurveLoop.Create(Curves(points,normal*(count==1 ? 0 : length/304.8*i/(count-1)))));
                    RebarFreeFormValidationResult validation;
                    bar = Rebar.CreateFreeForm(doc,barType,host,loops,out validation);
                    if (bar == null || validation != RebarFreeFormValidationResult.Success) throw new InvalidOperationException("FreeForm validation: "+validation);
                }
                else
                {
#if REVIT2027_OR_GREATER
                    bar = Rebar.CreateFromCurves(doc,RebarStyle.Standard,barType,host,normal,Curves(points,XYZ.Zero),new BarTerminationsData(doc),true,true);
#else
                    bar = Rebar.CreateFromCurves(doc,RebarStyle.Standard,barType,null,null,host,normal,Curves(points,XYZ.Zero),RebarHookOrientation.Right,RebarHookOrientation.Left,true,true);
#endif
                    if (bar == null) throw new InvalidOperationException("Revit could not create the requested shape.");
                    using (var a = bar.GetShapeDrivenAccessor())
                    {
                        if (rule == "Single") a.SetLayoutAsSingle();
                        if (rule == "FixedNumber") a.SetLayoutAsFixedNumber(qty,length/304.8,true,true,true);
                        if (rule == "MaximumSpacing") a.SetLayoutAsMaximumSpacing(spacing/304.8,length/304.8,true,true,true);
                    }
                }
                doc.Regenerate();
                var result = new { dry_run=dry, created_id=dry ? (long?)null : RevitCompat.GetId(bar.Id), host_id=RevitCompat.GetId(host.Id), bar_type_id=RevitCompat.GetId(barType.Id), mode, requested_layout=rule, quantity=bar.Quantity, distribution_length_mm=length, bend_diameter_mm=barType.StandardBendDiameter*304.8, native_type=bar.GetType().FullName, constraint_note=mode=="FreeForm" ? "Unconstrained free-form Rebar: edit geometry via RebarFreeFormAccessor.SetCurves; host-face constraints cannot be added later." : "Shape-driven native Rebar. Check/adjust host-face constraints in Revit.", design_check="Geometry only. Anchorage, laps, cover and clashes require project-specific review." };
                if (tx.Commit()!=TransactionStatus.Committed) return CommandResult.Fail("Revit rolled back the requested geometry: "+string.Join("; ",failures.Messages));
                if (dry) group.RollBack(); else group.Assimilate();
                return CommandResult.Ok(result);
            }
            }
        }

        private static IList<Curve> Curves(IList<XYZ> points,XYZ offset)
        {
            var curves=new List<Curve>();
            for(int i=1;i<points.Count;i++) curves.Add(Line.CreateBound(points[i-1]+offset,points[i]+offset));
            return curves;
        }
    }

    internal class RebarPathFailures : IFailuresPreprocessor
    {
        public readonly List<string> Messages=new List<string>();
        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            // Roll back warnings too: success must not hide invalid geometry/constraints.
            var failures=a.GetFailureMessages();
            Messages.AddRange(failures.Select(m=>m.GetDescriptionText()));
            return failures.Count>0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
