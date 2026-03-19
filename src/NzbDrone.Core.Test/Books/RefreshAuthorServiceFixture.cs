using System.Collections.Generic;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Books
{
    [TestFixture]
    public class RefreshAuthorServiceFixture : CoreTest<RefreshAuthorService>
    {
        private Author _author;

        [SetUp]
        public void SetUp()
        {
            _author = Builder<Author>.CreateNew()
                .With(a => a.Id = 1)
                .With(a => a.Path = "/books/Terry Pratchett")
                .Build();

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.GetAuthors(It.IsAny<List<int>>()))
                .Returns(new List<Author> { _author });

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.All())
                .Returns(new List<RootFolder>
                {
                    new RootFolder { Path = "/books" }
                });

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.RescanAfterRefresh)
                .Returns(RescanAfterRefreshType.Always);
        }

        [TearDown]
        public void TearDown()
        {
            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void rescan_scopes_to_author_path_not_root_folder()
        {
            Subject.Execute(new RefreshAuthorCommand(1, isNewAuthor: true));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(
                    q => q.Push(
                        It.Is<RescanFoldersCommand>(c =>
                            c.Folders.Count == 1 &&
                            c.Folders[0] == "/books/Terry Pratchett"),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(
                    q => q.Push(
                        It.Is<RescanFoldersCommand>(c =>
                            c.Folders.Contains("/books")),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Never);
        }

        [Test]
        public void rescan_falls_back_to_root_folder_when_author_has_no_path()
        {
            // Author with empty path — e.g. not yet written to disk
            _author.Path = string.Empty;

            Subject.Execute(new RefreshAuthorCommand(1, isNewAuthor: true));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(
                    q => q.Push(
                        It.Is<RescanFoldersCommand>(c =>
                            c.Folders.Contains("/books")),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(
                    q => q.Push(
                        It.Is<RescanFoldersCommand>(c =>
                            c.Folders.Contains("/books/Terry Pratchett")),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Never);
        }
    }
}
