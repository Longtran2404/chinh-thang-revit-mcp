using System;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests
{
    public class AppearanceEditsTests
    {
        [Theory]
        [InlineData("[{\"path\":[\"surface_roughness\"],\"value\":0.4}]")]
        [InlineData("[{\"path\":[\"generic_diffuse\"],\"value\":[0.2,0.3,0.4,1]}]")]
        [InlineData("[{\"path\":[\"surface_normal\"],\"bitmap\":{\"file\":\"C:/Textures/normal.png\",\"scaleXmm\":600,\"scaleYmm\":300,\"rotationDegrees\":90,\"invert\":false}}]")]
        public void Accepts_schema_properties_without_renaming_or_losing_channels(string json)
        {
            Assert.Single(AppearanceEdits.Parse(json));
        }

        [Theory]
        [InlineData("[{}]")]
        [InlineData("[{\"path\":[],\"value\":0.5}]")]
        [InlineData("[{\"path\":[\"roughness\"],\"value\":0.4,\"ignoredChannel\":1}]")]
        [InlineData("[{\"path\":[\"roughness\"],\"value\":null}]")]
        [InlineData("[{\"path\":[42],\"value\":0.4}]")]
        [InlineData("[{\"path\":[\"color\"],\"value\":1,\"bitmap\":{\"file\":\"C:/a.png\"}}]")]
        [InlineData("[{\"path\":[\"color\"],\"bitmap\":{\"file\":\"C:/a.png\",\"scaleXmm\":0}}]")]
        [InlineData("[{\"path\":[\"color\"],\"bitmap\":{\"file\":\"C:/a.png\",\"scaleYmm\":-10}}]")]
        [InlineData("[{\"path\":[\"color\"],\"bitmap\":{\"file\":\"C:/a.png\",\"normalMode\":\"guess\"}}]")]
        [InlineData("[{\"path\":[\"color\"],\"bitmap\":{\"file\":\"C:/a.png\",\"invert\":1}}]")]
        [InlineData("[{\"path\":[\"color\"],\"bitmap\":{\"file\":\"C:/a.png\",\"rotationDegrees\":\"90\"}}]")]
        public void Rejects_invalid_or_ambiguous_edits_instead_of_ignoring_them(string json)
        {
            Assert.ThrowsAny<Exception>(() => AppearanceEdits.Parse(json));
        }
    }
}
