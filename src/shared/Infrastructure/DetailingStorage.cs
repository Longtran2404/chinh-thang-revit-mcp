// Local, per-document component recipes. Never modifies unrelated elements.
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin
{
    public static class DetailingStorage
    {
        private static readonly Guid Id=new Guid("7c9d0cb5-bb77-4faa-99d7-a17bd5942a91");
        private static Schema GetSchema()
        {
            var schema=Schema.Lookup(Id);
            if(schema!=null) return schema;
            var b=new SchemaBuilder(Id);
            b.SetSchemaName("ChinhThangDetailingRecipeV1");b.SetReadAccessLevel(AccessLevel.Public);b.SetWriteAccessLevel(AccessLevel.Public);
            b.AddSimpleField("Json",typeof(string));return b.Finish();
        }
        public static JObject Read(Element e)
        {
            var schema=Schema.Lookup(Id);
            if(e==null || schema==null) return null;
            var entity=e.GetEntity(schema);
            return entity.IsValid() ? JObject.Parse(entity.Get<string>(schema.GetField("Json"))) : null;
        }
        public static void Write(Element e,JObject value)
        {
            var schema=GetSchema();var entity=new Entity(schema);
            entity.Set<string>(schema.GetField("Json"),value.ToString(Newtonsoft.Json.Formatting.None));e.SetEntity(entity);
        }
        public static DataStorage Profile(Document d) => new FilteredElementCollector(d).OfClass(typeof(DataStorage)).Cast<DataStorage>().FirstOrDefault(e=>e.Name=="CT_Detailing_Profile_V1");
    }
}
