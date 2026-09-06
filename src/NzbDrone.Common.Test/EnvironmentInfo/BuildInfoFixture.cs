using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Test.EnvironmentInfo
{
    [TestFixture]
    public class BuildInfoFixture
    {
        [Test]
        public void should_return_version()
        {
            // 0 when the assembly carries no version; otherwise this fork's major.
            BuildInfo.Version.Major.Should().BeOneOf(0, 11);
        }

        [Test]
        public void should_get_branch()
        {
            BuildInfo.Branch.Should().NotBe("unknown");
            BuildInfo.Branch.Should().NotBeNullOrWhiteSpace();
        }
    }
}
