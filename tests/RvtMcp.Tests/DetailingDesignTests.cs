using System;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests
{
    public class DetailingDesignTests
    {
        private static JObject Tcvn()=>JObject.Parse(@"{'standard':'TCVN5574:2018','operation':'Anchorage','steel_stress':'Tension','surface':'HotRolledRibbed','end_shape':'Straight','diameter_mm':16,'rs_mpa':350,'rbt_mpa':1.05,'as_required_mm2':201,'as_provided_mm2':201,'concrete_kind':'NormalWeight','spliced_percent':50}");
        private static JObject Ec()=>JObject.Parse(@"{'standard':'EN1992-1-1:2004','operation':'Anchorage','steel_stress':'Tension','surface':'HotRolledRibbed','end_shape':'Straight','diameter_mm':16,'sigma_sd_mpa':435,'fyd_mpa':435,'fctd_mpa':1.2,'concrete_kind':'NormalWeight','spliced_percent':50,'national_annex':'Project-approved design strengths','bond_condition':'Good'}");
        [Theory]
        [InlineData("Anchorage","Tension",50,535)]
        [InlineData("Anchorage","Compression",50,400)]
        [InlineData("Lap","Tension",50,640)]
        [InlineData("Lap","Tension",75,855)]
        [InlineData("Lap","Tension",100,1070)]
        [InlineData("Lap","Compression",50,480)]
        public void Tcvn_hand_calculated_lengths(string operation,string stress,int percent,double expected)
        {
            var p=Tcvn();p["operation"]=operation;p["steel_stress"]=stress;p["spliced_percent"]=percent;
            var r=DetailingDesign.Calculate(p);
            Assert.Equal(533.333333333,r.Value<double>("basic_length_mm"),6);
            Assert.Equal(expected,r.Value<double>("required_length_mm"));
            Assert.Equal(80,r.Value<double>("minimum_bend_diameter_mm"));
            Assert.False(r.Value<bool>("reduction_applied"));
        }
        [Theory]
        [InlineData("Anchorage",240)]
        [InlineData("Lap",320)]
        public void Minimum_length_controls_low_area_ratio(string operation,double expected)
        {var p=Tcvn();p["operation"]=operation;p["as_required_mm2"]=20;Assert.Equal(expected,DetailingDesign.Calculate(p).Value<double>("required_length_mm"));}
        [Fact]
        public void Fine_grained_concrete_uses_concrete_stress()
        {var p=Tcvn();p["concrete_kind"]="FineGrainedA";p["concrete_stress"]="Tension";Assert.Equal(695,DetailingDesign.Calculate(p).Value<double>("required_length_mm"));}
        [Theory]
        [InlineData("standard","TCVN5574:2012")]
        [InlineData("standard","EN1992-1-1:2023")]
        [InlineData("surface","Unknown")]
        [InlineData("concrete_kind","Unknown")]
        [InlineData("end_shape","Unknown")]
        public void Unsupported_designs_never_fall_back(string key,string value)
        {var p=Tcvn();p[key]=value;Assert.Throws<ArgumentException>(()=>DetailingDesign.Calculate(p));}
        [Theory]
        [InlineData("diameter_mm",34)]
        [InlineData("rs_mpa",0)]
        [InlineData("rbt_mpa",-1)]
        [InlineData("as_required_mm2",300)]
        public void Rejects_bad_strengths_and_diameter_bands(string key,double value)
        {var p=Tcvn();p[key]=value;Assert.Throws<ArgumentException>(()=>DetailingDesign.Calculate(p));}
        [Theory]
        [InlineData("Good","Anchorage",50,645)]
        [InlineData("Poor","Anchorage",50,925)]
        [InlineData("Good","Lap",50,915)]
        [InlineData("Good","Lap",100,970)]
        public void Ec_hand_calculated_lengths(string bond,string operation,int percent,double expected)
        {var p=Ec();p["bond_condition"]=bond;p["operation"]=operation;p["spliced_percent"]=percent;Assert.Equal(expected,DetailingDesign.Calculate(p).Value<double>("required_length_mm"));}
        [Fact]
        public void Ec_requires_design_basis_and_enforces_strength()
        {var p=Ec();p.Remove("national_annex");Assert.Throws<ArgumentException>(()=>DetailingDesign.Calculate(p));p=Ec();p["sigma_sd_mpa"]=500;Assert.Throws<ArgumentException>(()=>DetailingDesign.Calculate(p));}
        [Fact]
        public void Rejects_nonfinite_input()
        {var p=Tcvn();p["rs_mpa"]=double.NaN;Assert.Throws<ArgumentException>(()=>DetailingDesign.Calculate(p));}
        [Fact]
        public void Grid_is_cell_centred_and_rejects_unbounded_counts()
        {
            var grid=SlabSupportLayout.Grid(0,0,2000,1000,1000,1000);
            Assert.Equal(2,grid.Count);Assert.Equal(new[]{500.0,500.0},grid[0]);Assert.Equal(new[]{1500.0,500.0},grid[1]);
            Assert.Throws<ArgumentException>(()=>SlabSupportLayout.Grid(0,0,double.NaN,1000,1000,1000));
            Assert.Throws<ArgumentException>(()=>SlabSupportLayout.Grid(0,0,1000000,1000000,0.00001,1));
            Assert.Throws<ArgumentException>(()=>SlabSupportLayout.Grid(0,0,0,1000,1000,1000));
        }
        [Fact]
        public void Layout_requires_one_explicit_density_definition()
        {
            SlabSupportLayout.Validate(JObject.Parse(@"{'area_per_support_m2':1}"));
            SlabSupportLayout.Validate(JObject.Parse(@"{'spacing_x_mm':800,'spacing_y_mm':1000}"));
            Assert.Throws<ArgumentException>(()=>SlabSupportLayout.Validate(new JObject()));
            Assert.Throws<ArgumentException>(()=>SlabSupportLayout.Validate(JObject.Parse(@"{'area_per_support_m2':1,'spacing_x_mm':800}")));
            Assert.Throws<ArgumentException>(()=>SlabSupportLayout.Validate(JObject.Parse(@"{'spacing_x_mm':800}")));
        }
    }
}
