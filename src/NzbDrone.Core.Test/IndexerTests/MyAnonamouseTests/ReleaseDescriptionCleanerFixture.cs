using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.MyAnonamouse;

namespace NzbDrone.Core.Test.IndexerTests.MyAnonamouseTests
{
    [TestFixture]
    public class ReleaseDescriptionCleanerFixture
    {
        [TestCase(null, null)]
        [TestCase("", null)]
        [TestCase("   ", null)]
        public void should_return_null_for_empty_input(string input, string expected)
        {
            ReleaseDescriptionCleaner.Strip(input).Should().Be(expected);
        }

        // ---- BBCode ----
        [Test]
        public void should_strip_simple_bbcode_tags()
        {
            ReleaseDescriptionCleaner.Strip("[b]Bold[/b] and [i]italic[/i]")
                .Should().Be("Bold and italic");
        }

        [Test]
        public void should_strip_bbcode_tags_with_attributes()
        {
            ReleaseDescriptionCleaner.Strip("[size=4][b]Title[/b][/size]")
                .Should().Be("Title");
        }

        [Test]
        public void should_keep_bbcode_url_text_but_drop_the_tag()
        {
            ReleaseDescriptionCleaner.Strip("See [url=https://example.com]the site[/url] now")
                .Should().Be("See the site now");
        }

        [Test]
        public void should_remove_bbcode_img_tags_including_their_content()
        {
            ReleaseDescriptionCleaner.Strip("Before [img]https://example.com/cover.jpg[/img] after")
                .Should().Be("Before  after");
        }

        [Test]
        public void should_strip_bbcode_list_markup()
        {
            ReleaseDescriptionCleaner.Strip("[list][*]First[*]Second[/list]")
                .Should().Be("FirstSecond");
        }

        // ---- HTML ----
        [Test]
        public void should_strip_inline_html_tags_but_keep_their_text()
        {
            ReleaseDescriptionCleaner.Strip("the <em>Penguin Classics</em> edition")
                .Should().Be("the Penguin Classics edition");
        }

        [Test]
        public void should_convert_br_tags_to_line_breaks()
        {
            ReleaseDescriptionCleaner.Strip("One<br />Two<br>Three<br/>Four")
                .Should().Be("One\nTwo\nThree\nFour");
        }

        [Test]
        public void should_convert_paragraph_boundaries_to_line_breaks()
        {
            ReleaseDescriptionCleaner.Strip("<p>First para</p><p>Second para</p>")
                .Should().Be("First para\nSecond para");
        }

        [Test]
        public void should_remove_script_and_style_including_content()
        {
            ReleaseDescriptionCleaner.Strip("Before<script>alert('x');</script><style>.a{color:red}</style>After")
                .Should().Be("BeforeAfter");
        }

        [Test]
        public void should_clean_a_real_html_description()
        {
            var input =
                "<p>Alexandre Dumas' epic tale, the Penguin Classics edition of " +
                "<em>The Count of Monte Cristo</em> is translated by Robin Buss.<br /><br /></p>\n" +
                "<p>Thrown in prison for a crime he has not committed, Edmond Dant&egrave;s " +
                "reinvents himself as the Count of Monte Cristo.<br /><br /></p>";

            ReleaseDescriptionCleaner.Strip(input).Should().Be(
                "Alexandre Dumas' epic tale, the Penguin Classics edition of The Count of Monte Cristo " +
                "is translated by Robin Buss.\n\nThrown in prison for a crime he has not committed, " +
                "Edmond Dantès reinvents himself as the Count of Monte Cristo.");
        }

        // ---- Entities ----
        [Test]
        public void should_decode_html_entities()
        {
            ReleaseDescriptionCleaner.Strip("Tom &amp; Jerry &#8212; a tale")
                .Should().Be("Tom & Jerry — a tale");
        }

        [Test]
        public void should_clean_markup_that_arrives_html_escaped()
        {
            ReleaseDescriptionCleaner.Strip("&lt;p&gt;Escaped paragraph&lt;/p&gt;")
                .Should().Be("Escaped paragraph");
        }

        // Decoding turns &lt; and &gt; into < and >. A naive <[^>]+> strip would then eat
        // "< 10 and 20 >" and silently corrupt the sentence.
        [Test]
        public void should_not_corrupt_comparison_operators_after_decoding()
        {
            ReleaseDescriptionCleaner.Strip("Suitable when 5 &lt; 10 and 20 &gt; 15 holds")
                .Should().Be("Suitable when 5 < 10 and 20 > 15 holds");
        }

        // ---- Whitespace ----
        [Test]
        public void should_preserve_line_breaks()
        {
            ReleaseDescriptionCleaner.Strip("Line one\r\n[b]Line two[/b]")
                .Should().Be("Line one\nLine two");
        }

        [Test]
        public void should_collapse_excessive_blank_lines()
        {
            ReleaseDescriptionCleaner.Strip("One\n\n\n\n\nTwo")
                .Should().Be("One\n\nTwo");
        }

        [Test]
        public void should_return_null_when_only_bbcode_markup_remains()
        {
            ReleaseDescriptionCleaner.Strip("[img]https://example.com/x.jpg[/img]")
                .Should().BeNull();
        }

        [Test]
        public void should_return_null_when_only_html_markup_remains()
        {
            ReleaseDescriptionCleaner.Strip("<p><br /></p>")
                .Should().BeNull();
        }
    }
}
