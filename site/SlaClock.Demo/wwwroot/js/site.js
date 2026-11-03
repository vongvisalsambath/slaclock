// Static parts of the page: C# highlighting for the examples, and the "Use it in an app" tabs.
(function () {
  "use strict";
  var KW = "var|new|using|await|async|public|private|static|class|record|return|if|else|foreach|in|true|false|null|void|is";
  var TY = "BusinessCalendar|SlaTimer|SlaStatus|SlaState|SlaEvent|SlaEventKind|TimeSpan|DateOnly|DateTimeOffset|DayOfWeek|TimeRange|Task|Ticket|NewTicket|AppDb|TicketService|Assert|Fact";
  var re = new RegExp(
    "(//[^\\n]*)|(\"(?:\\\\.|[^\"\\\\])*\")|\\b(" + KW + ")\\b|\\b(" + TY + ")\\b|\\b(\\d+(?:\\.\\d+)?)\\b|(?<=\\.)([A-Z][A-Za-z0-9]*)(?=\\()",
    "g");
  var cls = ["c", "s", "k", "t", "n", "m"];
  function esc(s) { return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;"); }
  function highlight(text) {
    var out = "", last = 0, m;
    re.lastIndex = 0;
    while ((m = re.exec(text))) {
      out += esc(text.slice(last, m.index));
      for (var i = 1; i <= 6; i++) if (m[i] !== undefined) { out += '<span class="tok-' + cls[i - 1] + '">' + esc(m[0]) + "</span>"; break; }
      last = m.index + m[0].length;
    }
    return out + esc(text.slice(last));
  }
  document.querySelectorAll("code.cs").forEach(function (el) { el.innerHTML = highlight(el.textContent); });

  // tabs (WAI-ARIA pattern: arrows move between tabs)
  document.querySelectorAll("[data-tabs]").forEach(function (root) {
    var tabs = Array.prototype.slice.call(root.querySelectorAll('[role="tab"]'));
    function select(tab, focus) {
      tabs.forEach(function (t) {
        var on = t === tab;
        t.setAttribute("aria-selected", String(on));
        t.tabIndex = on ? 0 : -1;
        document.getElementById(t.getAttribute("aria-controls")).hidden = !on;
      });
      if (focus) tab.focus();
    }
    tabs.forEach(function (t, i) {
      t.addEventListener("click", function () { select(t, false); });
      t.addEventListener("keydown", function (e) {
        var n = e.key === "ArrowRight" ? i + 1 : e.key === "ArrowLeft" ? i - 1 : e.key === "Home" ? 0 : e.key === "End" ? tabs.length - 1 : null;
        if (n === null) return;
        e.preventDefault();
        select(tabs[(n + tabs.length) % tabs.length], true);
      });
    });
  });
})();
