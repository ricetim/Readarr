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
            release.Title.Should().Be("Kerrelyn Sparks - Love at Stake series [MP3]");
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
        public async Task should_parse_release_details()
        {
            var recentFeed = ReadAllText(@"Files/Indexers/MyAnonamouse/MyAnonamouse.json");

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, recentFeed)));

            var releases = await Subject.FetchRecent();
            var details = releases[0].Details;

            details.Should().NotBeNull();
            details.Narrators.Should().BeEquivalentTo("Abby Craden");
            details.FileCount.Should().Be(149);
            details.Series.Should().Be("Love at Stake (01-16)");
            details.Tags.Should().Be("unabridged mp3");
            details.Category.Should().Be("Audiobooks - Urban Fantasy");
            details.Description.Should().Be("Love at Stake - Books 1-16\n\nWelcome to the world of modern day vampires.");
        }

        [Test]
        public async Task should_handle_empty_narrator_and_series_objects()
        {
            var recentFeed = ReadAllText(@"Files/Indexers/MyAnonamouse/MyAnonamouse.json");

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, recentFeed)));

            var releases = await Subject.FetchRecent();

            // Entry 2 has "narrator_info": "{}", "series_info": "{}" and no description,
            // but still has numfiles/tags/catname, so Details is present but sparse.
            var details = releases[1].Details;

            details.Should().NotBeNull();
            details.Narrators.Should().BeEmpty();
            details.Series.Should().BeNull();
            details.Description.Should().BeNull();
            details.FileCount.Should().Be(1);
        }

        [Test]
        public async Task should_return_empty_list_when_no_results_found()
        {
            var noResults = "{\"error\":\"Nothing returned, out of 0\"}";

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, noResults)));

            var releases = await Subject.FetchRecent();

            releases.Should().BeEmpty();
        }

        [Test]
        public void should_throw_on_auth_error()
        {
            var authError = "{\"error\":\"Invalid session\"}";
            var request = new HttpRequest("https://www.myanonamouse.net/tor/js/loadSearchJSONbasic.php");
            var httpResponse = new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, authError);
            var indexerResponse = new IndexerResponse(new IndexerRequest(request), httpResponse);

            var parser = new MyAnonamouseParser();
            Action act = () => parser.ParseResponse(indexerResponse);
            act.Should().Throw<NzbDrone.Core.Indexers.Exceptions.IndexerException>()
               .WithMessage("*Invalid session*");
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
            release.Title.Should().Be("Patrick Rothfuss - The Name of the Wind [EPUB]");
            release.Author.Should().Be("Patrick Rothfuss");
            release.Guid.Should().Be("MAM-310000");
            release.IndexerFlags.HasFlag(IndexerFlags.Freeleech).Should().BeFalse();
        }

        [Test]
        public async Task should_parse_language_on_audiobook_release()
        {
            var recentFeed = ReadAllText(@"Files/Indexers/MyAnonamouse/MyAnonamouse.json");

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(
                    new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, recentFeed)));

            var releases = await Subject.FetchRecent();

            var release = (MyAnonamouseInfo)releases[0];
            release.Languages.Should().ContainSingle()
                .Which.Should().Be(NzbDrone.Core.Languages.Language.English);
        }

        [Test]
        public async Task should_parse_language_on_ebook_release()
        {
            var recentFeed = ReadAllText(@"Files/Indexers/MyAnonamouse/MyAnonamouse.json");

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(
                    new HttpResponse(r, new HttpHeader { ContentType = "application/json" }, recentFeed)));

            var releases = await Subject.FetchRecent();

            var release = (MyAnonamouseInfo)releases[1];
            release.Languages.Should().ContainSingle()
                .Which.Should().Be(NzbDrone.Core.Languages.Language.English);
        }
    }
}
