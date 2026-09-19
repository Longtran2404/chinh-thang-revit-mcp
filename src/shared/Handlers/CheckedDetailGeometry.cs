// Chinh Thang, 2026-09-19: real editable family geometry and solid containment.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin.Handlers
{
    internal static class CheckedDetailGeometry
    {
        public static XYZ Point(JToken token,string name,bool direction=false)
        {var v=SteelDetailInput.Vector(token,name,direction);return new XYZ(v[0],v[1],v[2]);}
        public static List<Solid> Solids(GeometryElement g)
        {
            var r=new List<Solid>();if(g==null)return r;
            foreach(var x in g){if(x is Solid s&&s.Volume>1e-10)r.Add(s);else if(x is GeometryInstance i)r.AddRange(Solids(i.GetInstanceGeometry()));}return r;
        }
        public static List<Solid> Solids(Element e)=>Solids(e.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}));
        public static FamilySymbol Symbol(Document d,long id)
        {
            var s=d.GetElement(RevitCompat.ToElementId(id)) as FamilySymbol;
            if(s==null||!s.Family.IsEditable||(s.Family.FamilyPlacementType!=FamilyPlacementType.OneLevelBased&&s.Family.FamilyPlacementType!=FamilyPlacementType.WorkPlaneBased))throw new ArgumentException("Use an editable unhosted or work-plane-based family symbol with native solid geometry.");
            if(!s.IsActive)s.Activate();return s;
        }
        public static void Length(FamilyInstance f,string name,double mm)
        {
            if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Explicit dimension parameter name required.");
            var matches=f.GetParameters(name);if(matches.Count>1)throw new ArgumentException("Ambiguous instance parameter: "+name);
            var p=matches.FirstOrDefault();
            if(p==null){var ts=f.Symbol.GetParameters(name);if(ts.Count!=1)throw new ArgumentException("Missing/ambiguous length parameter: "+name);p=ts[0];}
            if(p.StorageType!=StorageType.Double||p.Definition.GetDataType()!=SpecTypeId.Length)throw new ArgumentException(name+" must be a length parameter.");
            bool onType=p.Element is ElementType;
            if(p.IsReadOnly||onType){if(Math.Abs(p.AsDouble()*304.8-mm)>.01)throw new ArgumentException(name+" does not match the selected type. Duplicate/configure the type first; shared types are not changed.");}
            else p.Set(mm/304.8);
        }
        public static FamilyInstance Place(Document d,FamilySymbol s,XYZ point,XYZ axis)
        {
            if(s.Family.FamilyPlacementType==FamilyPlacementType.WorkPlaneBased){var normal=axis.Normalize();var u=Math.Abs(normal.Z)>.99?XYZ.BasisX:XYZ.BasisZ.CrossProduct(normal).Normalize();var v=normal.CrossProduct(u);var view=new FilteredElementCollector(d).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(x=>!x.IsTemplate);if(view==null)throw new ArgumentException("Work-plane placement needs an existing plan view.");var plane=d.Create.NewReferencePlane(point,point+u*5,-v,view);return d.Create.NewFamilyInstance(plane.GetReference(),point,u,s);}
            var f=d.Create.NewFamilyInstance(point,s,StructuralType.NonStructural);var z=axis.Normalize();var rotation=XYZ.BasisZ.CrossProduct(z);double angle=XYZ.BasisZ.AngleTo(z);
            if(angle>1e-8)ElementTransformUtils.RotateElement(d,f.Id,Line.CreateUnbound(point,rotation.GetLength()<1e-8?XYZ.BasisX:rotation.Normalize()),angle);return f;
        }
        public static void Contained(IEnumerable<Solid> items,List<Solid> host)
        {
            foreach(var original in items){Solid remaining=original;foreach(var h in host){remaining=BooleanOperationsUtils.ExecuteBooleanOperation(remaining,h,BooleanOperationsType.Difference);if(remaining.Volume<=1e-10)break;}if(remaining.Volume>1e-9)throw new ArgumentException("Anchor solid protrudes outside the concrete (including openings). No anchors were committed.");}
        }
        public static JArray Stiffeners(Document d,JObject request,XYZ node)
        {
            var rows=request["stiffeners"] as JArray??new JArray();if(rows.Count>20)throw new ArgumentException("At most 20 explicit stiffeners per node.");var result=new JArray();
            foreach(var token in rows){var p=token as JObject??throw new ArgumentException("Stiffener must be an object.");var s=Symbol(d,p.Value<long>("symbol_id"));var offset=Point(p["offset_mm"],"stiffener offset_mm")/304.8;
                if(offset.GetLength()>1000/304.8)throw new ArgumentException("Stiffener offset must be within 1000 mm of the computed node.");
                var f=Place(d,s,node+offset,Point(p["axis"],"stiffener axis",true));
                foreach(var key in new[]{"width","height","thickness"})Length(f,p.Value<string>(key+"_parameter"),SteelDetailInput.Positive(p,key+"_mm"));
                d.Regenerate();if(Solids(f).Count==0)throw new ArgumentException("Stiffener family has no physical solid.");
                f.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set("CT-STIFFENER-"+RevitCompat.GetId(f.Id));
                DetailingStorage.Write(f,new JObject{["kind"]="SteelStiffener",["request"]=p.DeepClone()});result.Add(new JObject{["id"]=RevitCompat.GetId(f.Id),["family"]=s.Family.Name,["thickness_mm"]=p["thickness_mm"],["placement_verified"]=true,["weld_fit_capacity_verified"]=false});
            }return result;
        }
    }
}
