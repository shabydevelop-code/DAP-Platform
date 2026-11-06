'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const ui = require('../../src/GuideMe.Instructor.Web/wwwroot/any-value-ui.js');
const main = fs.readFileSync(path.join(root, 'src/GuideMe.Instructor.Web/wwwroot/main.js'), 'utf8');
const html = fs.readFileSync(path.join(root, 'src/GuideMe.Instructor.Web/wwwroot/index.html'), 'utf8');

const webExact = {Strategy:'css', Value:'td:exact-text("ABC")'};
const webAny = {Strategy:'css', Value:'td:nonempty-text'};
const webBinding = {buildAny:()=>webAny.Value,build:value=>'td:exact-text('+JSON.stringify(value)+')'};
const windowsExact = {Strategy:'name',Value:'ABC'};
const windowsAny = {Strategy:'name-nonempty',Value:'*'};
const winAutomation = {Strategy:'automation-id',Value:'customer-number'};
const winAutomationAny = {Strategy:'automation-id-nonempty',Value:'customer-number'};

test('Web and Windows use exactly the same checkbox markup for each state', () => {
  for (const checked of [false,true]) {
    for (const runtime of ['Web','Windows']) {
      const locator = runtime === 'Web' ? webExact : windowsExact;
      const binding = runtime === 'Web' ? webBinding : undefined;
      assert.ok(ui.isEligible(runtime,locator,binding));
      const markup = ui.renderOption('primary-target-any',checked);
      assert.match(markup,/class="gm-any-value-option"/);
      assert.match(markup,/<span>כל ערך<\/span>/);
      assert.equal(markup.includes(' checked'),checked);
      // Markup does not accept runtime: it cannot diverge by runtime.
      assert.equal(markup,ui.renderOption('primary-target-any',checked));
    }
  }
});
test('Web switching to any and back preserves the CSS locator and text semantics', () => {
  assert.deepEqual(ui.buildUpdate('Web',webExact,true,'ABC',webBinding),
    {locatorStrategy:'css',locatorValue:webAny.Value});
  assert.deepEqual(ui.buildUpdate('Web',webAny,false,'ABC',webBinding),
    {locatorStrategy:'css',locatorValue:webExact.Value});
  assert.equal(ui.buildUpdate('Web',webAny,false,'',webBinding),null);
});
test('Windows switching to any and back preserves the locator semantics', () => {
  assert.deepEqual(ui.buildUpdate('Windows',windowsExact,true,'ABC'),
    {locatorStrategy:'name-nonempty',locatorValue:'*'});
  assert.deepEqual(ui.buildUpdate('Windows',windowsAny,false,'ABC'),
    {locatorStrategy:'name',locatorValue:'ABC'});
  assert.deepEqual(ui.buildUpdate('Windows',winAutomation,true,'ABC'),
    {locatorStrategy:'automation-id-nonempty',locatorValue:'customer-number'});
  assert.deepEqual(ui.buildUpdate('Windows',winAutomationAny,false,'ABC'),
    {locatorStrategy:'automation-id',locatorValue:'customer-number'});
  assert.equal(ui.buildUpdate('Windows',windowsAny,false,''),null);
});
test('The same shared renderer is wired into card, capture, and editor', () => {
  assert.equal((main.match(/GuideMeAnyValueUI\.renderOption\(/g)||[]).length,3);
  for (const id of ['primary-target-any','picked-content-any','edit-primary-target-any'])
    assert.ok(main.includes("renderOption('"+id+"'"),'Missing shared option '+id);
  assert.match(main,/GuideMeAnyValueUI\.buildUpdate\(/);
  assert.match(main,/GuideMeAnyValueUI\.isEligible\(/);
  assert.ok(!main.includes("windowsAnyEligible"), 'Runtime-specific card rendering returned');
  assert.ok(!main.includes('gm-any-value-option'), 'Markup must be authored only in shared renderer');
  assert.ok(!main.includes('<span>כל ערך</span>'), 'Duplicated checkbox labels are prohibited');
  assert.ok(html.indexOf('any-value-ui.js') < html.indexOf('main.js'));
});
test('Inputs are not allowed to inject arbitrary HTML into shared markup', () => {
  assert.throws(()=>ui.renderOption('x" onclick="oops',false));
  assert.equal(ui.isEligible('Windows',{Strategy:'css',Value:'div'}),false);
  assert.equal(ui.isEligible('Web',winAutomation,webBinding),false);
});
