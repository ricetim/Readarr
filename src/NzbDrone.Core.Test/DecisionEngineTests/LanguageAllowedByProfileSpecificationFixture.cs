using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class LanguageAllowedByProfileSpecificationFixture : CoreTest<LanguageAllowedByProfileSpecification>
    {
        private RemoteBook _remoteBook;

        [SetUp]
        public void Setup()
        {
            var fakeAuthor = Builder<Author>.CreateNew()
                .With(c => c.QualityProfile = new QualityProfile
                {
                    Cutoff = Quality.EPUB.Id,
                    AllowedLanguages = new List<Language>()
                })
                .Build();

            _remoteBook = new RemoteBook
            {
                Author = fakeAuthor,
                Release = new ReleaseInfo { Languages = new List<Language>() },
                ParsedBookInfo = new ParsedBookInfo
                {
                    Quality = new QualityModel(Quality.EPUB)
                }
            };
        }

        [Test]
        public void should_accept_when_profile_allowed_languages_is_empty()
        {
            // Empty = no preference = accept anything
            _remoteBook.Author.QualityProfile.Value.AllowedLanguages = new List<Language>();
            _remoteBook.Release.Languages = new List<Language> { Language.German };

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_release_languages_is_empty()
        {
            // Indexer provides no language data — don't punish it
            _remoteBook.Author.QualityProfile.Value.AllowedLanguages = new List<Language> { Language.English };
            _remoteBook.Release.Languages = new List<Language>();

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_release_language_matches_profile()
        {
            _remoteBook.Author.QualityProfile.Value.AllowedLanguages = new List<Language> { Language.English, Language.French };
            _remoteBook.Release.Languages = new List<Language> { Language.French };

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_when_release_language_not_in_profile()
        {
            _remoteBook.Author.QualityProfile.Value.AllowedLanguages = new List<Language> { Language.English };
            _remoteBook.Release.Languages = new List<Language> { Language.German };

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_when_no_release_language_intersects_profile()
        {
            _remoteBook.Author.QualityProfile.Value.AllowedLanguages = new List<Language> { Language.English, Language.French };
            _remoteBook.Release.Languages = new List<Language> { Language.German, Language.Spanish };

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeFalse();
        }
    }
}
