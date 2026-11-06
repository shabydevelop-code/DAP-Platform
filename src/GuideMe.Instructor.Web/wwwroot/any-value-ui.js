// Single presentation and transition contract for Web and Windows Instructor targets.
// Runtime-specific locator strategies remain data, never separate UI markup.
(function (root) {
  'use strict';
  function renderOption(id, checked) {
    if (!/^[a-z][a-z0-9-]*$/.test(id)) throw new Error('Invalid any-value control id');
    return '<label class="gm-any-value-option"><input type="checkbox" id="' + id +
      '"' + (checked ? ' checked' : '') + '><span>כל ערך</span></label>';
  }
  function isEligible(runtime, locator, binding) {
    if (!locator) return false;
    if (runtime === 'Windows')
      return ['name', 'automation-id', 'name-nonempty', 'automation-id-nonempty'].includes(locator.Strategy);
    return runtime === 'Web' && locator.Strategy === 'css' && typeof binding?.buildAny === 'function';
  }
  // Returns null when the author must supply a precise value via the existing editor.
  function buildUpdate(runtime, locator, checked, displayName, binding) {
    if (!isEligible(runtime, locator, binding)) throw new Error('Unsupported any-value target');
    if (runtime === 'Web') {
      const exact = String(displayName || '').trim();
      if (!checked && !exact) return null;
      return {locatorStrategy: 'css',
        locatorValue: checked ? binding.buildAny() : binding.build(exact)};
    }
    const strategy = checked
      ? (locator.Strategy.startsWith('automation-id') ? 'automation-id-nonempty' : 'name-nonempty')
      : (locator.Strategy === 'automation-id-nonempty' ? 'automation-id' : 'name');
    const value = strategy === 'name-nonempty' ? '*' :
      strategy === 'name' ? String(displayName || '').trim() : locator.Value;
    return value ? {locatorStrategy: strategy, locatorValue: value} : null;
  }
  const api = Object.freeze({renderOption, isEligible, buildUpdate});
  root.GuideMeAnyValueUI = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
