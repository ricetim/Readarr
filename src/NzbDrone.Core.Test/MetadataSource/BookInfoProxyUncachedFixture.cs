using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class BookInfoProxyUncachedFixture : CoreTest<BookInfoProxy>
    {
        [SetUp]
        public void SetUp()
        {
            var factory = new HttpRequestBuilder("http://test/{route}").CreateFactory();
            Mocker.GetMock<IMetadataRequestBuilder>()
                  .Setup(x => x.GetRequestBuilder())
                  .Returns(factory);
        }

        [Test]
        public void should_pass_kca_as_query_param_when_kca_known()
        {
            var meta = new AuthorMetadata { ForeignAuthorId = "3389", Kca = "kca://author/amzn1.gr.author.v1.Test" };
            Mocker.GetMock<IAuthorMetadataService>()
                  .Setup(x => x.FindById("3389"))
                  .Returns(meta);

            HttpRequest capturedRequest = null;
            Mocker.GetMock<IHttpClient>()
                  .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(r => capturedRequest = r)
                  .Returns(new HttpResponse(new HttpRequest("http://test/author/3389"), new HttpHeader(), string.Empty, HttpStatusCode.NotFound));

            try
            {
                Subject.GetAuthorInfo("3389", false);
            }
            catch
            {
            }

            capturedRequest.Should().NotBeNull();
            capturedRequest.Url.ToString().Should().Contain("kca=kca%3A%2F%2Fauthor%2Famzn1.gr.author.v1.Test");
        }

        [Test]
        public void should_not_retry_when_server_returns_valid_response()
        {
            Mocker.GetMock<IAuthorMetadataService>()
                  .Setup(x => x.FindById(It.IsAny<string>()))
                  .Returns((AuthorMetadata)null);

            var callCount = 0;
            Mocker.GetMock<IHttpClient>()
                  .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(_ => callCount++)
                  .Returns(new HttpResponse(new HttpRequest("http://test/author/0"), new HttpHeader(), string.Empty, HttpStatusCode.NotFound));

            try
            {
                Subject.GetAuthorInfo("0", false);
            }
            catch
            {
            }

            callCount.Should().Be(1);
        }
    }
}
