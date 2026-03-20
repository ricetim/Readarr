using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Bibliotik;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.BibliotikTests
{
    [TestFixture]
    public class BibliotikFixture : CoreTest<Bibliotik>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Definition = new IndexerDefinition
            {
                Name = "Bibliotik",
                Settings = new BibliotikSettings
                {
                    Username = "testuser",
                    Password = "testpass"
                }
            };
        }

        [Test]
        public async Task should_parse_recent_feed_from_bibliotik()
        {
            var recentFeed = ReadAllText(@"Files/Indexers/Bibliotik/bibliotik.html");

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(new HttpResponse(r, new HttpHeader { ContentType = "text/html; charset=utf-8" }, recentFeed)));

            var releases = await Subject.FetchRecent();

            releases.Should().HaveCount(3);
            releases[0].Should().BeOfType<BibliotikInfo>();
        }

        [Test]
        public async Task should_parse_ebook_entry()
        {
            var recentFeed = ReadAllText(@"Files/Indexers/Bibliotik/bibliotik.html");

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(new HttpResponse(r, new HttpHeader { ContentType = "text/html; charset=utf-8" }, recentFeed)));

            var releases = await Subject.FetchRecent();

            var release = (BibliotikInfo)releases[0];
            release.Title.Should().Be("Patrick Rothfuss - The Name of the Wind (2007) [EPUB]");
            release.Author.Should().Be("Patrick Rothfuss");
            release.Guid.Should().Be("Bibliotik-12345");
            release.DownloadUrl.Should().EndWith("/torrents/12345/download");
            release.InfoUrl.Should().EndWith("/torrents/12345");
            release.Size.Should().Be(5242880L);
            release.Seeders.Should().Be(15);
            release.Peers.Should().Be(18);
            release.PublishDate.Should().Be(new DateTime(2023, 4, 1, 12, 0, 0, DateTimeKind.Utc));
            release.DownloadProtocol.Should().Be(DownloadProtocol.Torrent);
        }

        [Test]
        public async Task should_parse_audiobook_entry()
        {
            var recentFeed = ReadAllText(@"Files/Indexers/Bibliotik/bibliotik.html");

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(new HttpResponse(r, new HttpHeader { ContentType = "text/html; charset=utf-8" }, recentFeed)));

            var releases = await Subject.FetchRecent();

            var release = (BibliotikInfo)releases[1];
            release.Title.Should().Be("Brandon Sanderson - The Way of Kings (2010) [MP3]");
            release.Author.Should().Be("Brandon Sanderson");
            release.Size.Should().Be(1073741824L);
        }

        [Test]
        public async Task should_fall_back_to_editor_link_when_no_author_link()
        {
            var recentFeed = ReadAllText(@"Files/Indexers/Bibliotik/bibliotik.html");

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => Task.FromResult(new HttpResponse(r, new HttpHeader { ContentType = "text/html; charset=utf-8" }, recentFeed)));

            var releases = await Subject.FetchRecent();

            var release = (BibliotikInfo)releases[2];
            release.Author.Should().Be("Dan Simmons");
        }

        [Test]
        public void should_throw_when_not_logged_in()
        {
            var loginPage = "<html><body><center>Login failed.</center><form><input name=\"username\"/></form></body></html>";
            var request = new HttpRequest("https://bibliotik.me/torrents/");
            var httpResponse = new HttpResponse(request, new HttpHeader { ContentType = "text/html" }, loginPage);
            var indexerResponse = new IndexerResponse(new IndexerRequest(request), httpResponse);

            var parser = new BibliotikParser("https://bibliotik.me");
            Action act = () => parser.ParseResponse(indexerResponse);
            act.Should().Throw<NzbDrone.Core.Indexers.Exceptions.IndexerException>()
               .WithMessage("*not logged in*");
        }
    }
}
