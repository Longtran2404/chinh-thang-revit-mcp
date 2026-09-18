using System;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests
{
    public class RebarPathInputTests
    {
        [Theory]
        [InlineData("[[0,0,300],[0,0,0],[3000,0,0],[3000,0,300]]",4)]
        [InlineData("[[0,0,0],[500,0,0],[2000,0,1500],[2500,0,1500]]",4)]
        [InlineData("[[0,0,0],[200,0,0],[200,0,200],[200,400,200],[200,400,0],[400,400,0]]",6)]
        public void Accepts_anchored_stair_and_spatial_chair_paths(string json,int count)
        { Assert.Equal(count,RebarPathInput.Points(json).Length); }

        [Theory]
        [InlineData("[]")]
        [InlineData("[[0,0,0]]")]
        [InlineData("[[0,0,0],[0,0,0]]")]
        [InlineData("[[0,0],[10,0,0]]")]
        [InlineData("[[0,0,0],[\"10\",0,0]]")]
        [InlineData("[[0,0,0],[100000001,0,0]]")]
        [InlineData("[[0,0,0],[10,0,0],[20,0,0]]")]
        [InlineData("[[0,0,0],[10,0,0],[0,0,0]]")]
        public void Rejects_invalid_or_redundant_geometry(string json)
        { Assert.Throws<ArgumentException>(()=>RebarPathInput.Points(json)); }

        [Theory]
        [InlineData("Single",2,0,0)]
        [InlineData("FixedNumber",1,1000,0)]
        [InlineData("FixedNumber",5,0,0)]
        [InlineData("FixedNumber",5,1000,100)]
        [InlineData("MaximumSpacing",1,1000,0)]
        [InlineData("MaximumSpacing",1,1000,0.1)]
        [InlineData("MaximumSpacing",1,1000,2000)]
        [InlineData("MaximumSpacing",5,1000,100)]
        public void Rejects_ambiguous_or_unbounded_distribution(string rule,int qty,double length,double spacing)
        { Assert.Throws<ArgumentException>(()=>RebarPathInput.Layout("ShapeDriven",rule,qty,length,spacing)); }

        [Fact]
        public void Accepts_explicit_layouts()
        {
            RebarPathInput.Layout("ShapeDriven","Single",1,0,0);
            RebarPathInput.Layout("ShapeDriven","FixedNumber",6,1000,0);
            RebarPathInput.Layout("FreeForm","MaximumSpacing",1,1000,200);
        }
    }
}
