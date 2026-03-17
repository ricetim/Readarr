const ebookFormats = new Set([
  'kindle edition', 'nook', 'ebook', 'epub', 'pdf'
]);

const audiobookFormats = new Set([
  'audiobook', 'audio cd', 'audio cassette', 'audible audio', 'cd-rom', 'mp3 cd'
]);

export function isEbookEdition(edition) {
  if (edition.isEbook) {
    return true;
  }
  return ebookFormats.has((edition.format || '').toLowerCase());
}

export function isAudiobookEdition(edition) {
  return audiobookFormats.has((edition.format || '').toLowerCase());
}

/**
 * Classify a book using server-computed flags (preferred) or edition array (fallback).
 * Returns { isEbook: bool, isAudiobook: bool }
 */
export function classifyBook(book) {
  if (book.hasEbookEdition !== undefined || book.hasAudiobookEdition !== undefined) {
    return {
      isEbook: book.hasEbookEdition === true,
      isAudiobook: book.hasAudiobookEdition === true
    };
  }

  // Fallback: classify from editions array if flags are missing
  const editions = book.editions || [];
  return {
    isEbook: editions.some(isEbookEdition),
    isAudiobook: editions.some(isAudiobookEdition)
  };
}
