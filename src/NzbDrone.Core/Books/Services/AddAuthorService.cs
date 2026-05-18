using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Books
{
    public interface IAddAuthorService
    {
        Author AddAuthor(Author newAuthor, bool doRefresh = true);
        List<Author> AddAuthors(List<Author> newAuthors, bool doRefresh = true);
    }

    public class AddAuthorService : IAddAuthorService
    {
        private readonly IAuthorService _authorService;
        private readonly IAuthorMetadataService _authorMetadataService;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IAddAuthorValidator _addAuthorValidator;
        private readonly Logger _logger;

        public AddAuthorService(IAuthorService authorService,
                                IAuthorMetadataService authorMetadataService,
                                IBuildFileNames fileNameBuilder,
                                IAddAuthorValidator addAuthorValidator,
                                Logger logger)
        {
            _authorService = authorService;
            _authorMetadataService = authorMetadataService;
            _fileNameBuilder = fileNameBuilder;
            _addAuthorValidator = addAuthorValidator;
            _logger = logger;
        }

        public Author AddAuthor(Author newAuthor, bool doRefresh = true)
        {
            Ensure.That(newAuthor, () => newAuthor).IsNotNull();

            newAuthor = AddSkyhookData(newAuthor);
            newAuthor = SetPropertiesAndValidate(newAuthor);

            _logger.Info("Adding Author {0} Path: [{1}]", newAuthor, newAuthor.Path);

            // add metadata
            _authorMetadataService.Upsert(newAuthor.Metadata.Value);
            newAuthor.AuthorMetadataId = newAuthor.Metadata.Value.Id;

            // add the author itself
            return _authorService.AddAuthor(newAuthor, doRefresh);
        }

        public List<Author> AddAuthors(List<Author> newAuthors, bool doRefresh = true)
        {
            var added = DateTime.UtcNow;
            var authorsToAdd = new List<Author>();

            foreach (var s in newAuthors)
            {
                try
                {
                    var author = AddSkyhookData(s);
                    author = SetPropertiesAndValidate(author);
                    author.Added = added;
                    authorsToAdd.Add(author);
                }
                catch (Exception ex)
                {
                    // Catch Import Errors for now until we get things fixed up
                    _logger.Error(ex, "Failed to import id: {0} - {1}", s.Metadata.Value.ForeignAuthorId, s.Metadata.Value.Name);
                }
            }

            // add metadata
            _authorMetadataService.UpsertMany(authorsToAdd.Select(x => x.Metadata.Value).ToList());
            authorsToAdd.ForEach(x => x.AuthorMetadataId = x.Metadata.Value.Id);

            return _authorService.AddAuthors(authorsToAdd, doRefresh);
        }

        private Author AddSkyhookData(Author newAuthor)
        {
            // Skip the blocking bookinfo fetch at add time. The search result already
            // provides sufficient metadata for manual adds, while import lists may only
            // provide a foreign ID and author name. Normalize the minimum metadata set
            // required by the DB and let RefreshAuthorCommand fill in the rest.
            var metadata = newAuthor.Metadata.Value;

            metadata.Name = metadata.Name.CleanSpaces();

            if (metadata.TitleSlug.IsNullOrWhiteSpace())
            {
                metadata.TitleSlug = metadata.ForeignAuthorId;
            }

            if (metadata.Name.IsNotNullOrWhiteSpace())
            {
                metadata.SortName ??= metadata.Name.ToLower();
                metadata.NameLastFirst ??= metadata.Name.ToLastFirst();
                metadata.SortNameLastFirst ??= metadata.NameLastFirst.ToLower();
            }

            metadata.Kca ??= string.Empty;

            return newAuthor;
        }

        private Author SetPropertiesAndValidate(Author newAuthor)
        {
            var path = newAuthor.Path;
            if (string.IsNullOrWhiteSpace(path))
            {
                var folderName = _fileNameBuilder.GetAuthorFolder(newAuthor);
                path = Path.Combine(newAuthor.RootFolderPath, folderName);
            }

            // Disambiguate author path if it exists already
            if (_authorService.AuthorPathExists(path))
            {
                if (newAuthor.Metadata.Value.Disambiguation.IsNotNullOrWhiteSpace())
                {
                    path += $" ({newAuthor.Metadata.Value.Disambiguation})";
                }

                if (_authorService.AuthorPathExists(path))
                {
                    var basepath = path;
                    var i = 0;
                    do
                    {
                        i++;
                        path = basepath + $" ({i})";
                    }
                    while (_authorService.AuthorPathExists(path));
                }
            }

            newAuthor.Path = path;
            newAuthor.CleanName = newAuthor.Metadata.Value.Name.CleanAuthorName();
            newAuthor.Added = DateTime.UtcNow;

            if (newAuthor.AddOptions != null && newAuthor.AddOptions.Monitor == MonitorTypes.None)
            {
                newAuthor.Monitored = false;
            }

            var validationResult = _addAuthorValidator.Validate(newAuthor);

            if (!validationResult.IsValid)
            {
                throw new ValidationException(validationResult.Errors);
            }

            return newAuthor;
        }
    }
}
