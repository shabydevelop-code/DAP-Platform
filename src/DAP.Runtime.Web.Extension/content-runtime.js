(() => {
  "use strict";

  if (globalThis.__dapWebRuntime) return;

  const normalize = value => String(value ?? "").replace(/\s+/g, " ").trim();

  function byText(value) {
    const wanted = normalize(value);
    return [...document.querySelectorAll("body *")]
      .filter(el => normalize(el.textContent) === wanted);
  }

  function byLabel(value) {
    const wanted = normalize(value);
    const result = [];
    for (const label of document.querySelectorAll("label")) {
      if (normalize(label.textContent) !== wanted) continue;
      if (label.htmlFor) {
        const target = document.getElementById(label.htmlFor);
        if (target) result.push(target);
      } else {
        const target = label.querySelector("input,select,textarea,button");
        if (target) result.push(target);
      }
    }
    return result;
  }

  function implicitRole(el) {
    const tag = el.tagName.toLowerCase();
    if (tag === "button") return "button";
    if (tag === "a" && el.hasAttribute("href")) return "link";
    if (tag === "select") return "combobox";
    if (tag === "textarea") return "textbox";
    if (tag === "input") {
      const type = (el.getAttribute("type") || "text").toLowerCase();
      if (["button","submit","reset"].includes(type)) return "button";
      if (type === "checkbox") return "checkbox";
      if (type === "radio") return "radio";
      return "textbox";
    }
    return null;
  }

  function createCandidates(locator) {
    const strategy = locator.strategy.trim().toLowerCase();
    if (strategy === "css") return [...document.querySelectorAll(locator.value)];
    if (strategy === "text") return byText(locator.value);
    if (strategy === "label") return byLabel(locator.value);
    if (strategy === "role") {
      const role = locator.value.trim().toLowerCase();
      return [...document.querySelectorAll("body *")]
        .filter(el => (el.getAttribute("role") || implicitRole(el)) === role);
    }
    throw new Error("Unsupported Web locator strategy '" + locator.strategy + "'.");
  }

  function matchesAnchor(el, anchor) {
    if (anchor.locator.strategy.toLowerCase() !== "css")
      throw new Error("Initial Web anchor relations support CSS anchors only.");

    const selector = anchor.locator.value;
    switch (String(anchor.relation).toLowerCase()) {
      case "ancestor":
      case "context": return !!el.closest(selector);
      case "descendant": return !!el.querySelector(selector);
      case "sibling":
        return !!(el.parentElement &&
          [...el.parentElement.children].some(x => x !== el && x.matches(selector)));
      case "nearby":
        return !!(el.parentElement && el.parentElement.querySelector(selector));
      default: return false;
    }
  }

  function resolveTarget(descriptor) {
    const matches = createCandidates(descriptor.locator)
      .filter(el => (descriptor.anchors || []).every(a => matchesAnchor(el, a)));

    if (matches.length === 0) return { status: "notFound", count: 0 };
    if (matches.length > 1) return { status: "ambiguous", count: matches.length };

    const el = matches[0];
    const rect = el.getBoundingClientRect();
    return {
      status: "resolved",
      count: 1,
      element: el,
      rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height }
    };
  }

  const listeners = new Set();
  const observer = new MutationObserver(records => {
    for (const listener of listeners) listener(records);
  });

  const start = () => {
    const root = document.documentElement;
    if (!root) return requestAnimationFrame(start);
    observer.observe(root, {
      subtree: true, childList: true, attributes: true, characterData: true
    });
  };

  start();

  globalThis.__dapWebRuntime = {
    version: "0.1.0",
    resolveTarget,
    onMutation(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    }
  };

  document.dispatchEvent(new CustomEvent("dap:web-runtime-ready", {
    detail: { version: globalThis.__dapWebRuntime.version }
  }));
})();
