using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.MyAnonamouse;

namespace NzbDrone.Core.Test.IndexerTests.MyAnonamouseTests
{
    [TestFixture]
    public class BbCodeCleanerFixture
    {
        [TestCase(null, null)]
        [TestCase("", null)]
        [TestCase("   ", null)]
        public void should_return_null_for_empty_input(string input, string expected)
        {
            BbCodeCleaner.Strip(input).Should().Be(expected);
        }

        [Test]
        public void should_strip_simple_tags()
        {
            BbCodeCleaner.Strip("[b]Bold[/b] and [i]italic[/i]")
                .Should().Be("Bold and italic");
        }

        [Test]
        public void should_strip_tags_with_attributes()
        {
            BbCodeCleaner.Strip("[size=4][b]Title[/b][/size]")
                .Should().Be("Title");
        }

        [Test]
        public void should_keep_url_text_but_drop_the_tag()
        {
            BbCodeCleaner.Strip("See [url=https://example.com]the site[/url] now")
                .Should().Be("See the site now");
        }

        [Test]
        public void should_remove_img_tags_including_their_content()
        {
            BbCodeCleaner.Strip("Before [img]https://example.com/cover.jpg[/img] after")
                .Should().Be("Before  after");
        }

        [Test]
        public void should_preserve_line_breaks()
        {
            BbCodeCleaner.Strip("Line one\r\n[b]Line two[/b]")
                .Should().Be("Line one\nLine two");
        }

        [Test]
        public void should_collapse_excessive_blank_lines()
        {
            BbCodeCleaner.Strip("One\n\n\n\n\nTwo")
                .Should().Be("One\n\nTwo");
        }

        [Test]
        public void should_decode_html_entities()
        {
            BbCodeCleaner.Strip("Tom &amp; Jerry &#8212; a tale")
                .Should().Be("Tom & Jerry — a tale");
        }

        [Test]
        public void should_strip_list_markup()
        {
            BbCodeCleaner.Strip("[list][*]First[*]Second[/list]")
                .Should().Be("FirstSecond");
        }

        [Test]
        public void should_return_null_when_only_markup_remains()
        {
            BbCodeCleaner.Strip("[img]https://example.com/x.jpg[/img]")
                .Should().BeNull();
        }
    }
}
