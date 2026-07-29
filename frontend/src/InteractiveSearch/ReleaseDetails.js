import PropTypes from 'prop-types';
import React, { useCallback, useState } from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import styles from './ReleaseDetails.css';

function ReleaseDetails(props) {
  const {
    narrators,
    fileCount,
    description,
    series,
    tags,
    category
  } = props;

  const [isDescriptionExpanded, setIsDescriptionExpanded] = useState(false);

  const onToggleDescription = useCallback(() => {
    setIsDescriptionExpanded((value) => !value);
  }, []);

  const rows = [];

  if (narrators && narrators.length > 0) {
    rows.push([translate('Narrator'), narrators.join(', ')]);
  }

  if (fileCount != null) {
    rows.push([translate('Files'), `${fileCount}`]);
  }

  if (series) {
    rows.push([translate('Series'), series]);
  }

  if (category) {
    rows.push([translate('Category'), category]);
  }

  if (tags) {
    rows.push([translate('Tags'), tags]);
  }

  return (
    <div className={styles.details}>
      <div className={styles.grid}>
        {
          rows.map(([label, value]) => {
            return [
              <div key={`${label}-label`} className={styles.label}>
                {label}
              </div>,
              <div key={`${label}-value`}>
                {value}
              </div>
            ];
          })
        }
      </div>

      {
        description ?
          <div className={styles.description}>
            <div className={isDescriptionExpanded ? undefined : styles.clamped}>
              {description}
            </div>

            <Link
              className={styles.toggle}
              onPress={onToggleDescription}
            >
              {isDescriptionExpanded ? translate('ShowLess') : translate('ShowMore')}
            </Link>
          </div> :
          null
      }
    </div>
  );
}

ReleaseDetails.propTypes = {
  narrators: PropTypes.arrayOf(PropTypes.string),
  fileCount: PropTypes.number,
  description: PropTypes.string,
  series: PropTypes.string,
  tags: PropTypes.string,
  category: PropTypes.string
};

export default ReleaseDetails;
