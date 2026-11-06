'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const ui = require('../../src/GuideMe.Instructor.Web/wwwroot/any-value-ui.js');
const main = fs.readFileSync(path.join(root, 'src/GuideMe.Instructor.Web/wwwroot/main.js'), 'utf8');
const html = fs.readFileSync(path.join(root, 'src/GuideMe.Instructor.Web/wwwroot/index.html'), 'utf8');

test('Web and Windows share the exact same edit-mode checkbox markup', () => {
  for (const checked of [false, true]) {
    const markup = ui.renderOption('edit-primary-target-any', checked);
    assert.match(markup, /class="gm-any-value-option"/);
    assert.match(markup, /<span>כל ערך<\/span>/);
    assert.equal(markup.includes(' checked'), checked);
    assert.equal(markup, ui.renderOption('edit-primary-target-any', checked));
  }
});
test('Target summary is read-only until the Edit action is clicked', () => {
  const summaryStart = main.indexOf("'<div id=\"primary-target-summary\">'");
  const summaryEnd = main.indexOf("'</div>' +", summaryStart);
  assert.ok(summaryStart >= 0 && summaryEnd > summaryStart, 'Target summary not found');
  const summary = main.slice(summaryStart, summaryEnd);
  assert.ok(summary.includes('targetValueDescription'), 'Saved matching mode is not displayed');
  assert.ok(!summary.includes('renderOption('), 'Editable control leaked into summary');
  assert.ok(!summary.includes('<input'), 'Input leaked into summary');
  assert.ok(!main.includes("$('primary-target-any')"), 'Direct summary editing handler returned');
  assert.ok(!main.includes("renderOption('primary-target-any'"), 'Direct checkbox returned');
});
test('The edit button alone opens the target editor', () => {
  assert.match(main, /id="bind-target-variable">עריכה<\/button>/);
  assert.match(main, /bindTarget\.onclick = \(\) => editPrimaryTargetValue\(/);
  assert.match(main, /function editPrimaryTargetValue\(/);
  assert.match(main, /renderOption\('edit-primary-target-any', !!binding\.any\)/);
  assert.match(main, /save\.onclick=async\(\)=>\{/);
  assert.match(main, /cancel\.onclick=restore/);
});
test('Any-value capture and edit modes share one renderer across runtimes', () => {
  assert.equal((main.match(/GuideMeAnyValueUI\.renderOption\(/g) || []).length, 2);
  for (const id of ['picked-content-any', 'edit-primary-target-any'])
    assert.ok(main.includes("renderOption('"+id+"'"), 'Missing shared option ' + id);
  assert.ok(!main.includes('gm-any-value-option'), 'Duplicated checkbox markup');
  assert.ok(!main.includes('<span>כל ערך</span>'), 'Duplicated checkbox text');
  assert.ok(html.indexOf('any-value-ui.js') < html.indexOf('main.js'));
});
test('Shared renderer rejects unsafe markup identifiers', () => {
  assert.throws(() => ui.renderOption('x" onclick="oops', false));
});
