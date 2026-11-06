// Native-only bootstrap. Prepare immutable spelling tables without document text
// before the helper reports ready, so changing vocabulary has no first-edit cost.
export * from '../../build/native-core/src/index.js';
import {ambiguityFor} from '../../build/native-core/src/ambiguity-lookup.js';
export function prepareSelectedVocabulary(language) {
  ambiguityFor(language, false).prepare();
}
