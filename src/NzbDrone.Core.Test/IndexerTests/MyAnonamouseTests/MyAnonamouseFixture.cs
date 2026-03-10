using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.MyAnonamouse;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.MyAnonamouseTests
{
    [TestFixture]
    public class MyAnonamouseFixture : CoreTest<MyAnonamouse>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Definition = new IndexerDefinition()
            {
                Name = "MyAnonamouse",
                Settings = new MyAnonamouseSettings { Cookie = "test_cookie_value" }
            };
        }

        [Test]
        public async Task should_parse_recent_feed_from_mam()
        {
            var recentFeed = ReadAllText(@"Files/Indexers/MyAnonamouse/MyAnonamouse.json");

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, recentFeed)));

            var releases = await Subject.FetchRecent();

            releases.Should().HaveCount(2);
            releases[0].Should().BeOfType<MyAnonamouseInfo>();

            var release = (MyAnonamouseInfo)releases[0];
            release.Title.Should().Be("Love at Stake series");
            release.Author.Should().Be("Kerrelyn Sparks");
            release.Guid.Should().Be("MAM-273200");
            release.DownloadUrl.Should().Be("https://www.myanonamouse.net/tor/download.php?tid=273200");
            release.InfoUrl.Should().Be("https://www.myanonamouse.net/t/273200");
            release.Size.Should().Be(6324306932L);
            release.Seeders.Should().Be(15);
            release.Peers.Should().Be(18);
            release.PublishDate.Should().Be(new DateTime(2023, 4, 1, 12, 0, 0, DateTimeKind.Utc));
            release.DownloadProtocol.Should().Be(DownloadProtocol.Torrent);
            release.IndexerFlags.HasFlag(IndexerFlags.Freeleech).Should().BeTrue();
        }

        [Test]
        public async Task should_parse_ebook_entry()
        {
            var recentFeed = ReadAllText(@"Files/Indexers/MyAnonamouse/MyAnonamouse.json");

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, recentFeed)));

            var releases = await Subject.FetchRecent();

            var release = (MyAnonamouseInfo)releases[1];
            release.Title.Should().Be("The Name of the Wind");
            release.Author.Should().Be("Patrick Rothfuss");
            release.Guid.Should().Be("MAM-310000");
            release.IndexerFlags.HasFlag(IndexerFlags.Freeleech).Should().BeFalse();
        }
    }
}
