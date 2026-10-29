(function () {
  "use strict";
  var state = { status: null, role: "local", activePage: "markdown", folders: { markdown: null },
    selections: { markdown: new Set() }, outputs: { markdown: null }, folderSignatures: {}, markdownBusy: false, themePreference: null };
  var toast = document.getElementById("toast"), toastTimer = null;
  var darkModeQuery = window.matchMedia ? window.matchMedia("(prefers-color-scheme: dark)") : null;
  var infoTooltip = document.getElementById("infoTooltip"), infoTooltipTarget = null;
  loadThemePreference(); wireInfoTips(); registerServiceWorker();
  document.querySelector('[data-page-panel="markdown"]').hidden = false;
  document.querySelectorAll('[data-page="markdown"]').forEach(function (el) { el.classList.add("is-active"); el.setAttribute("aria-current", "page"); });
  document.querySelectorAll(".choose-files").forEach(function (el) { el.addEventListener("click", function () { chooseFiles("markdown"); }); });
  document.querySelectorAll(".choose-folder").forEach(function (el) { el.addEventListener("click", function () { chooseFolder("markdown"); }); });
  document.querySelectorAll("[data-selection-action]").forEach(function (el) { el.addEventListener("click", function () { setAllSelections("markdown", el.dataset.selectionAction === "all"); }); });
  document.querySelectorAll("[data-theme-toggle]").forEach(function (el) { el.addEventListener("click", toggleTheme); });
  document.getElementById("convertMarkdown").addEventListener("click", convertMarkdown);
  document.getElementById("openMarkdownOutput").addEventListener("click", function () { openOutput("markdown", this); });
  setPickerDisabled(true);
  loadInitialState();
  async function loadInitialState() {
    try {
      state.status = await requestJson("/api/status"); state.role = state.status.role;
      document.documentElement.classList.add("role-local"); document.getElementById("appShell").dataset.role = "local";
      document.getElementById("deviceName").textContent = state.status.device;
      setCapability("statusWord", state.status.capabilities.word ? "Detected" : "Not detected", state.status.capabilities.word);
      setCapability("statusOcr", state.status.capabilities.windowsOcr ? "Yes" : "No", state.status.capabilities.windowsOcr);
      applyFolder("markdown", await requestJson("/api/folder?mode=markdown"));
      setPickerDisabled(state.role !== "local"); updateMarkdownAction(); updateOutputAction("markdown");
    } catch (error) { showToast(errorMessage(error)); }
  }
  // Desktop activation refreshes the existing window's input.
  window.File2MDActivate = function () { loadInitialState(); };
  function loadThemePreference() {
    var preference = null;
    try {
      var stored = window.localStorage.getItem("ferry-theme");
      if (stored === "light" || stored === "dark") {
        preference = stored;
      }
    } catch (_) {
      // The toggle still works for this session when storage is unavailable.
    }
    applyThemePreference(preference, false);
    if (darkModeQuery) {
      var updateFromSystem = function () {
        if (!state.themePreference) {
          updateThemeToggle();
        }
      };
      if (typeof darkModeQuery.addEventListener === "function") {
        darkModeQuery.addEventListener("change", updateFromSystem);
      } else if (typeof darkModeQuery.addListener === "function") {
        darkModeQuery.addListener(updateFromSystem);
      }
    }
  }

  function toggleTheme() {
    applyThemePreference(isDarkThemeActive() ? "light" : "dark", true);
  }

  function applyThemePreference(preference, persist) {
    state.themePreference = preference;
    if (preference) {
      document.documentElement.dataset.theme = preference;
    } else {
      document.documentElement.removeAttribute("data-theme");
    }
    if (persist) {
      try {
        window.localStorage.setItem("ferry-theme", preference);
      } catch (_) {
        // Keep the selected theme for this session when storage is unavailable.
      }
    }
    updateThemeToggle();
  }

  function isDarkThemeActive() {
    if (state.themePreference) {
      return state.themePreference === "dark";
    }
    return Boolean(darkModeQuery && darkModeQuery.matches);
  }

  function updateThemeToggle() {
    var dark = isDarkThemeActive();
    document.querySelectorAll("[data-theme-toggle]").forEach(function (button) {
      button.setAttribute("aria-pressed", dark ? "true" : "false");
      button.setAttribute("aria-label", dark ? "Switch to light theme" : "Switch to dark theme");
    });
  }

  function wireInfoTips() {
    document.querySelectorAll("[data-info-tip]").forEach(function (target) {
      target.addEventListener("pointerenter", function () {
        showInfoTip(target);
      });
      target.addEventListener("pointerleave", function () {
        hideInfoTip(target, false);
      });
      target.addEventListener("focus", function () {
        showInfoTip(target);
      });
      target.addEventListener("blur", function () {
        hideInfoTip(target, false);
      });
      target.addEventListener("click", function () {
        showInfoTip(target);
      });
      target.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
          hideInfoTip(target, true);
          target.blur();
        }
      });
    });
    document.addEventListener("pointerdown", function (event) {
      if (infoTooltipTarget && !infoTooltipTarget.contains(event.target)) {
        hideInfoTip(infoTooltipTarget, true);
      }
    });
    document.addEventListener("scroll", function () {
      if (infoTooltipTarget) {
        hideInfoTip(infoTooltipTarget, true);
      }
    }, true);
    window.addEventListener("resize", function () {
      if (infoTooltipTarget) {
        hideInfoTip(infoTooltipTarget, true);
      }
    });
  }

  function showInfoTip(target) {
    var text = target.dataset.infoTip;
    if (!text) {
      return;
    }
    infoTooltipTarget = target;
    infoTooltip.textContent = text;
    infoTooltip.hidden = false;
    infoTooltip.style.left = "0px";
    infoTooltip.style.top = "0px";

    var margin = 10;
    var gap = 8;
    var targetBounds = target.getBoundingClientRect();
    var tipBounds = infoTooltip.getBoundingClientRect();
    var left = targetBounds.left + targetBounds.width / 2 - tipBounds.width / 2;
    left = Math.max(margin, Math.min(window.innerWidth - tipBounds.width - margin, left));
    var top = targetBounds.top - tipBounds.height - gap;
    if (top < margin) {
      top = targetBounds.bottom + gap;
    }
    top = Math.max(margin, Math.min(window.innerHeight - tipBounds.height - margin, top));
    infoTooltip.style.left = Math.round(left) + "px";
    infoTooltip.style.top = Math.round(top) + "px";
  }

  function hideInfoTip(target, force) {
    if (infoTooltipTarget !== target) {
      return;
    }
    if (!force && (document.activeElement === target || target.matches(":hover"))) {
      return;
    }
    infoTooltipTarget = null;
    infoTooltip.hidden = true;
  }

  function isPage(value) {
    return value === "markdown";
  }

  function setCapability(id, text, positive) {
    var element = document.getElementById(id);
    element.textContent = text;
    element.classList.toggle("is-muted", !positive);
  }

  function applyFolder(mode, snapshot, preserveTransfer) {
    var selectedSource = snapshot && snapshot.sourceKind !== "none" ? snapshot : null;
    state.folders[mode] = selectedSource;
    state.folderSignatures[mode] = folderSignature(snapshot);
    var files = selectedSource && Array.isArray(selectedSource.files) ? selectedSource.files : [];
    state.selections[mode] = new Set(files.filter(function (file) {
      return file.markdownSupported;
    }).map(function (file) { return file.name; }));

    document.querySelectorAll("[data-folder-path='" + mode + "']").forEach(function (element) {
      var displayPath = selectedSource && selectedSource.path ? selectedSource.path : "No input selected";
      element.textContent = displayPath;
      element.title = selectedSource && selectedSource.path ? selectedSource.path : "";
    });

    if (mode === "markdown") {
      clearMarkdownResult();
      updateMarkdownOutput();
    }
    renderFiles(mode);
  }

  function renderFiles(mode) {
    var files = selectableFiles(mode);
    renderSelectableFiles(mode + "Files", files, mode);
    updateSelectionActions(mode, files);
    updateSummary(mode);
  }

  function selectableFiles(mode) {
    var folder = state.folders[mode];
    var files = folder && Array.isArray(folder.files) ? folder.files : [];
    return files.filter(function (file) { return file.markdownSupported; });
  }

  function renderSelectableFiles(containerId, files, mode) {
    var container = document.getElementById(containerId);
    container.replaceChildren();

    if (files.length === 0) {
      appendMessage(
        container,
        mode === "markdown" ? "No files are available for Markdown conversion" : "No files",
        "empty-state");
      return;
    }

    var fragment = document.createDocumentFragment();
    files.forEach(function (file) {
      var row = document.createElement("button");
      row.type = "button";
      row.className = "file-row";
      row.dataset.fileName = file.name;
      row.title = file.name;
      row.setAttribute("role", "checkbox");
      row.setAttribute("aria-checked", state.selections[mode].has(file.name) ? "true" : "false");
      row.classList.toggle("is-selected", state.selections[mode].has(file.name));
      row.appendChild(fileNameCell(file));
      row.appendChild(textCell(formatBytes(file.size), "file-size"));
      row.appendChild(textCell(formatDate(file.modifiedUtc), "file-date"));
      row.addEventListener("click", function () {
        toggleSelection(mode, file.name, row);
      });
      fragment.appendChild(row);
    });
    container.appendChild(fragment);
  }

  function fileNameCell(file) {
    var cell = document.createElement("span");
    cell.className = "file-name";
    var check = document.createElement("span");
    check.className = "book-check";
    check.textContent = "✓";
    check.setAttribute("aria-hidden", "true");
    cell.appendChild(check);
    cell.appendChild(fileBadge(file));
    cell.appendChild(textCell(file.name, "file-name-text"));
    return cell;
  }

  function fileBadge(file) {
    var badge = document.createElement("span");
    badge.className = "file-badge kind-" + String(file.kind || "other").toLowerCase();
    badge.textContent = file.badge || "FILE";
    badge.setAttribute("aria-hidden", "true");
    return badge;
  }

  function textCell(text, className) {
    var cell = document.createElement("span");
    cell.className = className;
    cell.textContent = text;
    return cell;
  }

  function svgUse(symbol, className) {
    var svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("class", className);
    svg.setAttribute("aria-hidden", "true");
    var use = document.createElementNS("http://www.w3.org/2000/svg", "use");
    use.setAttribute("href", "#" + symbol);
    svg.appendChild(use);
    return svg;
  }

  function toggleSelection(mode, fileName, row) {
    var selected = state.selections[mode].has(fileName);
    if (selected) {
      state.selections[mode].delete(fileName);
    } else {
      state.selections[mode].add(fileName);
    }
    row.classList.toggle("is-selected", !selected);
    row.setAttribute("aria-checked", !selected ? "true" : "false");
    updateSelectionActions(mode, selectableFiles(mode));
    updateSummary(mode);
    if (mode === "markdown") {
      updateMarkdownOutput();
      clearMarkdownResult();
    }
  }

  function setAllSelections(mode, select) {
    if (mode !== "markdown") {
      return;
    }
    selectableFiles(mode).forEach(function (file) {
      if (select) {
        state.selections[mode].add(file.name);
      } else {
        state.selections[mode].delete(file.name);
      }
    });
    renderFiles(mode);
    if (mode === "markdown") {
      updateMarkdownOutput();
      clearMarkdownResult();
    }
  }

  function updateSelectionActions(mode, files) {
    var allButton = document.querySelector(
      "[data-selection-mode='" + mode + "'][data-selection-action='all']");
    var noneButton = document.querySelector(
      "[data-selection-mode='" + mode + "'][data-selection-action='none']");
    if (!allButton || !noneButton) {
      return;
    }
    var selectedCount = files.filter(function (file) {
      return state.selections[mode].has(file.name);
    }).length;
    allButton.disabled = files.length === 0 || selectedCount === files.length;
    noneButton.disabled = selectedCount === 0;
  }

  function updateSummary(mode) {
    var folder = state.folders[mode];
    var panel = document.querySelector("[data-page-panel='" + mode + "']");
    var files = folder && Array.isArray(folder.files) ? folder.files : [];
    var selected = files.filter(function (file) {
      return state.selections[mode].has(file.name);
    });
    panel.querySelector("[data-summary-count]").textContent = String(selected.length);
    panel.querySelector("[data-summary-size]").textContent = formatBytes(selected.reduce(function (total, file) {
      return total + file.size;
    }, 0));

    var typeContainer = panel.querySelector("[data-summary-types]");
    typeContainer.replaceChildren();
    var order = ["PDF", "Word", "Excel", "PowerPoint", "Text", "Other"];
    order.forEach(function (kind) {
      var count = selected.filter(function (file) { return file.kind === kind; }).length;
      if (count === 0) {
        return;
      }
      var pill = document.createElement("i");
      pill.className = "summary-pill";
      pill.textContent = kind + " " + count;
      typeContainer.appendChild(pill);
    });
    if (mode === "markdown") {
      updateMarkdownAction();
    }
  }

  async function chooseFolder(mode) {
    if (!state.status || state.role === "remote" || !isPage(mode)) {
      return;
    }

    await openNativePicker(
      mode,
      "/api/pick-folder?mode=" + encodeURIComponent(mode),
      { method: "POST" },
      "Folder selected");
  }

  async function chooseFiles(mode) {
    if (!state.status || state.role === "remote") {
      return;
    }

    await openNativePicker(
      mode,
      "/api/pick-files",
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ mode: mode })
      },
      "Files selected");
  }

  async function openNativePicker(mode, url, options, successMessage) {
    setPickerDisabled(true);
    try {
      var result = await requestJson(url, options);
      if (!result || result.cancelled) {
        return;
      }
      if (!result.folder) {
        throw new Error("Could not load the selected files or folder.");
      }
      applyFolder(mode, result.folder);
      showToast(successMessage);
    } catch (error) {
      showToast(errorMessage(error));
    } finally {
      setPickerDisabled(!state.status || state.role !== "local");
    }
  }

  function setPickerDisabled(disabled) {
    document.querySelectorAll(".choose-folder, .choose-files").forEach(function (button) {
      button.disabled = disabled;
    });
  }

  async function convertMarkdown() {
    if (state.markdownBusy || state.role !== "local") {
      return;
    }

    var files = Array.from(state.selections.markdown);
    if (files.length === 0 && !markdownOmissionsOnly()) {
      return;
    }

    var button = document.getElementById("convertMarkdown");
    state.markdownBusy = true;
    updateMarkdownAction();

    try {
      var excludeLargeFiles = await chooseLargeMarkdownFiles();
      if (excludeLargeFiles === null) { return; }
      clearMarkdownResult();
      button.setAttribute("aria-busy", "true");
      button.textContent = "Converting to Markdown…";
      var result = await requestJson("/api/markdown", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ files: files, combine: true, excludeLargeFiles: excludeLargeFiles })
      });

      var resultRow = document.getElementById("markdownResult");
      var resultPath = document.getElementById("markdownResultPath");
      resultPath.textContent = result.outputPath;
      resultPath.title = result.outputPath;
      resultRow.hidden = false;
      document.getElementById("markdownOutput").textContent = result.outputPath;
      state.outputs.markdown = result.outputPath;
      clearModeSelection("markdown");
      updateOutputAction("markdown");

      if (result.convertedCount === 0 && result.failedCount === 0) {
        showToast("Omitted file list saved");
      } else if (result.failedCount > 0) {
        showToast("Converted: " + result.convertedCount + ". Could not read: " + result.failedCount + ".");
      } else {
        showToast("Files converted to Markdown: " + result.convertedCount);
      }
    } catch (error) {
      showToast(errorMessage(error));
    } finally {
      state.markdownBusy = false;
      button.removeAttribute("aria-busy");
      button.textContent = "Convert to Markdown";
      updateMarkdownAction();
    }
  }

  function chooseLargeMarkdownFiles() {
    var folder = state.folders.markdown;
    var large = folder.files.filter(function (file) {
      return file.markdownSupported && state.selections.markdown.has(file.name)
        && file.size > state.status.markdownLargeFileBytes;
    });
    if (large.length === 0) { return Promise.resolve(false); }
    var dialog = document.getElementById("markdownLargeDialog");
    var list = document.getElementById("markdownLargeFiles");
    list.replaceChildren();
    var total = 0;
    large.forEach(function (file) {
      var row = document.createElement("li");
      row.appendChild(textCell(file.name, "large-file-name"));
      row.appendChild(textCell(formatBytes(file.size), "large-file-size mono"));
      list.appendChild(row);
      total += file.size;
    });
    document.getElementById("markdownLargeTotal").textContent =
      "Files over 1 MiB: " + large.length + " (total: " + formatBytes(total) + ").";
    dialog.returnValue = "";
    return new Promise(function (resolve) {
      dialog.addEventListener("close", function () {
        resolve(dialog.returnValue === "exclude" ? true : dialog.returnValue === "include" ? false : null);
      }, { once: true });
      dialog.showModal();
    });
  }

  function updateMarkdownAction() {
    var button = document.getElementById("convertMarkdown");
    var omissionsOnly = markdownOmissionsOnly();
    button.disabled = state.markdownBusy
      || state.role !== "local"
      || (state.selections.markdown.size === 0 && !omissionsOnly);
    if (!state.markdownBusy) {
      button.textContent = omissionsOnly ? "Export omitted file list" : "Convert to Markdown";
    }
  }

  function markdownOmissionsOnly() {
    var folder = state.folders.markdown;
    return folder && Array.isArray(folder.files) && folder.files.length > 0
      && folder.files.every(function (file) { return !file.markdownSupported; });
  }

  function clearModeSelection(mode) {
    state.folders[mode] = null;
    state.selections[mode] = new Set();
    document.querySelectorAll("[data-folder-path='" + mode + "']").forEach(function (element) {
      element.textContent = "No input selected";
      element.title = "";
    });
    renderFiles(mode);
  }

  function updateOutputAction(kind) {
    var id = "openMarkdownOutput";
    var button = document.getElementById(id);
    button.disabled = state.role !== "local" || !state.outputs[kind];
  }

  async function openOutput(kind, button) {
    if (state.role !== "local" || button.disabled) {
      return;
    }
    button.disabled = true;
    try {
      await requestJson("/api/open-output", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ kind: kind })
      });
    } catch (error) {
      showToast(errorMessage(error));
    } finally {
      updateOutputAction(kind);
    }
  }

  function updateMarkdownOutput() {
    var folder = state.folders.markdown;
    if (!folder) {
      return;
    }

    var files = folder.files.filter(function (file) {
      return state.selections.markdown.has(file.name);
    });
    var output;
    if (folder.sourceKind === "files" && files.length === 1) {
      var name = files[0].name;
      var dot = name.lastIndexOf(".");
      var base = dot > 0 ? name.substring(0, dot) : name;
      if (files[0].extension === ".md" || files[0].extension === ".markdown") {
        base += ".converted";
      }
      output = base + ".md";
    } else {
      output = folderLeaf(folder.path) + ".md";
    }
    var display = previewOutputPath(folder, output);
    document.getElementById("markdownOutput").textContent = display;
    document.getElementById("markdownOutput").title = display;
  }

  function clearMarkdownResult() {
    document.getElementById("markdownResult").hidden = true;
    document.getElementById("markdownResultPath").textContent = "";
  }

  function folderSignature(snapshot) {
    return JSON.stringify(snapshot || null);
  }

  function registerServiceWorker() {
    if (!("serviceWorker" in navigator) || !window.isSecureContext) {
      return;
    }
    window.addEventListener("load", function () {
      navigator.serviceWorker.register(
        "/service-worker.js?v=__File2MD_BUILD_ID__",
        { scope: "/", updateViaCache: "none" }).catch(function () {});
    });
  }

  async function requestJson(url, options, timeoutMs) {
    var settings = Object.assign({}, options || {});
    var controller = timeoutMs ? new AbortController() : null;
    var originalSignal = settings.signal;
    var forwardAbort = function () { if (controller) controller.abort(); };
    var timer = null;
    if (controller) {
      settings.signal = controller.signal;
      if (originalSignal) {
        if (originalSignal.aborted) controller.abort();
        else originalSignal.addEventListener("abort", forwardAbort, { once: true });
      }
      timer = window.setTimeout(function () { controller.abort(); }, timeoutMs);
    }
    try {
      var response = await fetch(url, settings);
      var body;
      try { body = await response.json(); } catch (_) { body = null; }
      if (!response.ok || body === null) {
        var error = new Error(body && body.error ? body.error : "Could not connect to File2MD.");
        error.status = response.ok ? 502 : response.status;
        throw error;
      }
      return body;
    } finally {
      window.clearTimeout(timer);
      if (originalSignal && controller) originalSignal.removeEventListener("abort", forwardAbort);
    }
  }

  function setListMessage(id, message, className) {
    var container = document.getElementById(id);
    container.replaceChildren();
    appendMessage(container, message, className);
  }

  function appendMessage(container, message, className) {
    var element = document.createElement("div");
    element.className = className;
    element.textContent = message;
    container.appendChild(element);
  }

  function showToast(message) {
    window.clearTimeout(toastTimer);
    toast.textContent = message;
    toast.hidden = false;
    toastTimer = window.setTimeout(function () {
      toast.hidden = true;
    }, 4200);
  }

  function errorMessage(error) {
    return error && error.message ? error.message : "File2MD encountered an error.";
  }

  function folderLeaf(path) { var parts = String(path || "output").split(/[\\/]/).filter(Boolean); return parts.length ? parts[parts.length - 1] : "output"; }
  function pad(value) { return String(value).padStart(2, "0"); }
  function previewOutputPath(folder, fileName) {
    var target = folder.sourceKind === "files"
      && Array.isArray(folder.files)
      && folder.files.length === 1
      ? folder.files[0].name
      : folderLeaf(folder.path);
    return ".\\output\\" + target + "_YYYYMMDD-HHmm\\" + fileName;
  }

  function formatBytes(value) {
    var bytes = Number(value) || 0;
    if (bytes < 1024) {
      return bytes + " B";
    }
    var units = ["KiB", "MiB", "GiB", "TiB"];
    var size = bytes;
    var unit = -1;
    do {
      size /= 1024;
      unit++;
    } while (size >= 1024 && unit < units.length - 1);
    var digits = size >= 100 ? 0 : size >= 10 ? 1 : 1;
    return size.toLocaleString("en-US", { maximumFractionDigits: digits }) + " " + units[unit];
  }

  function formatDate(value) {
    var date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return "";
    }
    return pad(date.getMonth() + 1) + "/" + pad(date.getDate()) + " " + pad(date.getHours()) + ":" + pad(date.getMinutes());
  }

}());
