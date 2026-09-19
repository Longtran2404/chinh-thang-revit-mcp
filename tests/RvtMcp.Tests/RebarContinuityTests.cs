using RvtMcp.Plugin;
using Xunit;
namespace RvtMcp.Tests;
public class RebarContinuityTests
{
    [Fact] public void Detects158mmColumnGap(){var r=RebarContinuityMath.Compare(new[]{0d,0,29},new[]{0d,0,3671},new[]{0d,0,3829},new[]{0d,0,6821});Assert.Equal(0,r[0]);Assert.Equal(-158,r[1],6);}
    [Fact] public void DetectsPartialOverlap(){var r=RebarContinuityMath.Compare(new[]{0d,0,0},new[]{1000d,0,0},new[]{500d,0,0},new[]{1500d,0,0});Assert.Equal(500,r[1],6);}
    [Fact] public void OppositeDirectionSameResult(){var r=RebarContinuityMath.Compare(new[]{0d,0,0},new[]{1000d,0,0},new[]{1500d,0,0},new[]{500d,0,0});Assert.Equal(500,r[1],6);}
    [Fact] public void OffsetLapIsNotCoincident(){var r=RebarContinuityMath.Compare(new[]{0d,0,0},new[]{1000d,0,0},new[]{500d,18,0},new[]{1500d,18,0});Assert.Equal(18,r[0],6);Assert.Equal(500,r[1],6);}
    [Fact] public void CrossingBarsAreOutsideParallelScope()=>Assert.Null(RebarContinuityMath.Compare(new[]{0d,0,0},new[]{1000d,0,0},new[]{500d,-500,0},new[]{500d,500,0}));
    [Fact] public void TouchingEndsHaveZeroOverlap(){var r=RebarContinuityMath.Compare(new[]{0d,0,0},new[]{1000d,0,0},new[]{1000d,0,0},new[]{2000d,0,0});Assert.Equal(0,r[1],6);}
    [Fact] public void DegenerateSegmentIsNotAudited()=>Assert.Null(RebarContinuityMath.Compare(new[]{0d,0,0},new[]{0d,0,0},new[]{0d,0,0},new[]{10d,0,0}));
}
