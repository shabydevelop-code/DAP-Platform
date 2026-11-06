// Shared Instructor control markup for Web and Windows.
// The read-only step summary never renders this control.
(function (root) {
  'use strict';
  function renderOption(id, checked) {
    if (!/^[a-z][a-z0-9-]*$/.test(id)) throw new Error('Invalid any-value control id');
    return '<label class="gm-any-value-option"><input type="checkbox" id="' + id +
      '"' + (checked ? ' checked' : '') + '><span>כל ערך</span></label>';
  }
  const api = Object.freeze({renderOption});
  root.GuideMeAnyValueUI = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
