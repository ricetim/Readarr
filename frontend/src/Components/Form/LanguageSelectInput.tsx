import React, { useCallback } from 'react';
import { useSelector } from 'react-redux';
import { createSelector } from 'reselect';
import AppState from 'App/State/AppState';
import EnhancedSelectInput from './EnhancedSelectInput';

interface Language {
  id: number;
  name: string;
}

const selectLanguageValues = (selectedLanguages: Language[]) =>
  createSelector(
    (state: AppState) => state.settings.languages,
    (languages) => {
      const value = selectedLanguages.map(({ id }) => id);
      const values = languages.items.map(({ id, name }: Language) => ({
        key: id,
        value: name,
      }));
      return { value, values };
    }
  );

interface LanguageSelectInputProps {
  name: string;
  value: Language[];
  onChange(payload: { name: string; value: Language[] }): void;
}

function LanguageSelectInput(props: LanguageSelectInputProps) {
  const { value = [], onChange } = props;
  const { value: selectedIds, values } = useSelector(
    selectLanguageValues(value)
  );

  const onChangeWrapper = useCallback(
    ({ name, value: selectedIdList }: { name: string; value: number[] }) => {
      const languages = selectedIdList.map((id) => ({
        id,
        name: values.find((v) => v.key === id)?.value ?? '',
      }));
      onChange({ name, value: languages });
    },
    [onChange, values]
  );

  return (
    <EnhancedSelectInput
      {...props}
      value={selectedIds}
      values={values}
      onChange={onChangeWrapper}
    />
  );
}

export default LanguageSelectInput;
