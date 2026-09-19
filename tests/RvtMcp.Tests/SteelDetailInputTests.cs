using System;
using Newtonsoft.Json.Linq;
using RvtMcp.Plugin;
using Xunit;
public class SteelDetailInputTests
{
    private static JObject Valid()=>JObject.Parse(@"{'diameter_mm':20,'length_mm':250,'diameter_parameter':'D','length_parameter':'L','positions_mm':[[0,0,0]],'axis':[0,0,-1]}");
    [Fact] public void ExplicitDimensionsAndDownwardAxisAccepted()=>Assert.Single(SteelDetailInput.Anchors(Valid()));
    [Theory][InlineData(0)][InlineData(-1)][InlineData(double.NaN)][InlineData(double.PositiveInfinity)]
    public void InvalidDiameterRejected(double diameter){var p=Valid();p["diameter_mm"]=diameter;Assert.Throws<ArgumentException>(()=>SteelDetailInput.Anchors(p));}
    [Fact] public void StringDiameterRejected(){var p=Valid();p["diameter_mm"]="M20";Assert.Throws<ArgumentException>(()=>SteelDetailInput.Anchors(p));}
    [Fact] public void DuplicateLocationsRejected(){var p=Valid();((JArray)p["positions_mm"]).Add(new JArray(0,0,0));Assert.Throws<ArgumentException>(()=>SteelDetailInput.Anchors(p));}
    [Fact] public void ZeroAxisRejected(){var p=Valid();p["axis"]=new JArray(0,0,0);Assert.Throws<ArgumentException>(()=>SteelDetailInput.Anchors(p));}
    [Fact] public void ExposureIsNotSilentlyPermitted(){var p=Valid();p["allow_exposed"]=true;Assert.Throws<ArgumentException>(()=>SteelDetailInput.Anchors(p));}
    [Fact] public void MomentNeedsDesignReference(){Assert.Throws<ArgumentException>(()=>SteelDetailInput.Behavior(JObject.Parse("{'connection_behavior':'Moment'}")));}
    [Fact] public void ReviewedMomentRemainsExplicit(){Assert.Equal("Moment",SteelDetailInput.Behavior(JObject.Parse("{'connection_behavior':'Moment','design_detail_reference':'SK-01'}")));}
    [Fact] public void InvalidBehaviorRejected()=>Assert.Throws<ArgumentException>(()=>SteelDetailInput.Behavior(JObject.Parse("{'connection_behavior':'AutoRigid'}")));
    private static JObject Layout()=>JObject.Parse(@"{'standard':'TCVN5575:2024','joint':'Bearing','accuracy':'B','edge':'Cut','bolt_diameter_mm':20,'hole_diameter_mm':22,'outer_thinnest_plate_mm':20,'fy_mpa':235,'width_mm':300,'height_mm':650,'force_axis':'Y','holes_mm':[[64,70],[236,70],[64,240],[236,240],[64,410],[236,410],[64,580],[236,580]]}");
    [Fact] public void Table43RegularGridPasses()=>Assert.True(SteelDetailInput.CheckBoltLayout(Layout()).Value<bool>("passed"));
    [Fact] public void Table43UsesHoleDiameterNotBoltDiameter(){var p=Layout();p["holes_mm"]=JArray.Parse("[[42,70],[258,70],[42,580],[258,580]]");Assert.False(SteelDetailInput.CheckBoltLayout(p).Value<bool>("passed"));}
    [Fact] public void Table43RejectsUnsupportedStandard(){var p=Layout();p["standard"]="EC3";Assert.Throws<ArgumentException>(()=>SteelDetailInput.CheckBoltLayout(p));}
    [Fact] public void Table43RejectsFourMillimetreClearance(){var p=Layout();p["hole_diameter_mm"]=24;Assert.False(SteelDetailInput.CheckBoltLayout(p).Value<bool>("passed"));}
    [Fact] public void Table43ThinOuterPlateFailsMaximumPitch(){var p=Layout();p["outer_thinnest_plate_mm"]=6;Assert.False(SteelDetailInput.CheckBoltLayout(p).Value<bool>("passed"));}
}
