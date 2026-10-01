/*
    Behaviour every screen gets. Loaded by the layout, so it runs on the change
    set screens as well as the price revision.

    Everything here is progressive enhancement: each screen works with the
    script blocked, and this only shortens the gap between a click and the
    page's answer.
*/
(function () {
    "use strict";

    /*
        A toast appears after the page reloads, which is too late to say
        anything about the click that caused it. In between there is a second
        or so where a save looks like it did nothing, and the honest response
        to that is to press the button again.

        So the button says what it is doing and stops accepting clicks. The
        change is deferred by a tick so the browser has already collected the
        form - disabling a submit button before that drops its name and value
        from the post, which is how named handlers get lost.
    */
    /*
        Marks the page as scripted. CSS keyed on this hides the Apply button,
        because only now can a select submit itself. Doing it the other way -
        hiding Apply in the markup and showing it if the script fails - cannot
        work: markup does not know whether the script ran.
    */
    document.documentElement.classList.add("js");

    var autos = document.querySelectorAll("[data-autosubmit]");

    for (var a = 0; a < autos.length; a++) {
        autos[a].addEventListener("change", function (e) {
            if (e.target.form) {
                e.target.form.submit();
            }
        });
    }

    /*
        A row menu is a <details>, which opens and closes by itself. What it
        does not do is close when you click elsewhere, and a menu left hanging
        over the next row is worse than no menu.
    */
    document.addEventListener("click", function (e) {
        var menus = document.querySelectorAll("details.rowmenu[open]");

        for (var m = 0; m < menus.length; m++) {
            if (!menus[m].contains(e.target)) {
                menus[m].open = false;
            }
        }
    });

    document.addEventListener("keydown", function (e) {
        if (e.key !== "Escape") {
            return;
        }

        var menus = document.querySelectorAll("details.rowmenu[open]");
        for (var m = 0; m < menus.length; m++) {
            menus[m].open = false;
        }

        // Escape dismisses the toast as well.
        var openToast = document.getElementById("toast");
        if (openToast) {
            openToast.remove();
        }
    });

    /*
        The bell keeps itself up to date.

        It used to be counted only when a page was drawn, so a notification
        raised while somebody sat on a screen - air-ticketing on the products
        list when the product team sends a tour over - did not appear until
        they happened to navigate.
    */
    var bell = document.querySelector("a.bell");

    if (bell && window.fetch) {
        var refreshBell = function () {
            fetch("/Core/Notifications/Count", { credentials: "same-origin", cache: "no-store" })
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (data) {
                    if (!data || typeof data.unread !== "number") {
                        return;
                    }

                    var unread = data.unread;
                    var badge = bell.querySelector(".bell__count");
                    var label = unread === 0
                        ? "Notifications"
                        : unread + (unread === 1 ? " unread notification" : " unread notifications");

                    bell.classList.toggle("bell--unread", unread > 0);
                    bell.setAttribute("title", label);
                    bell.setAttribute("aria-label", label);

                    if (unread > 0) {
                        if (!badge) {
                            badge = document.createElement("span");
                            badge.className = "bell__count";
                            badge.setAttribute("aria-hidden", "true");
                            bell.appendChild(badge);
                        }
                        badge.textContent = unread > 9 ? "9+" : String(unread);
                    } else if (badge) {
                        badge.parentNode.removeChild(badge);
                    }
                })
                .catch(function () { /* the badge is a courtesy; never break the page */ });
        };

        window.setInterval(refreshBell, 20000);
        document.addEventListener("visibilitychange", function () {
            if (!document.hidden) {
                refreshBell();
            }
        });
    }

    /*
        The toast's cross. Removes the element rather than setting [hidden],
        because .toast is display:flex and that beats the hidden attribute -
        which is why pressing the cross used to do nothing.
    */
    document.addEventListener("click", function (e) {
        var trigger = e.target.closest ? e.target.closest("[data-dismiss]") : null;
        if (!trigger) {
            return;
        }

        var target = document.getElementById(trigger.getAttribute("data-dismiss"));
        if (target) {
            target.remove();
        }
    });

    /*
        Side panels.

        The panel's content is on the page either way. With this script blocked
        it is ordinary sections at the bottom and the trigger is a plain
        #anchor to them, which is why the markup is not hidden server-side:
        markup cannot know whether the script ran.

        What this adds is the panel - opened over the page, closed by Escape,
        by the scrim, or by its own button.
    */
    var panels = document.querySelectorAll(".panel");

    if (panels.length > 0) {
        var scrim = document.createElement("div");
        scrim.className = "scrim";
        scrim.hidden = true;
        document.body.appendChild(scrim);

        var openPanel = null;
        var opener = null;

        var closePanel = function () {
            if (!openPanel) {
                return;
            }

            openPanel.classList.remove("panel--open");
            scrim.hidden = true;
            document.documentElement.classList.remove("panel-open");
            openPanel = null;

            // Back where they were. Losing your place in the page because you
            // looked at something and closed it again is its own small insult.
            if (opener) {
                opener.focus();
                opener = null;
            }
        };

        var showPanel = function (panel, trigger) {
            closePanel();

            openPanel = panel;
            opener = trigger;

            panel.classList.add("panel--open");
            scrim.hidden = false;
            document.documentElement.classList.add("panel-open");

            var close = panel.querySelector("[data-panel-close]");
            if (close) {
                close.focus();
            }
        };

        document.addEventListener("click", function (e) {
            var trigger = e.target.closest ? e.target.closest("[data-panel]") : null;

            if (trigger) {
                var panel = document.getElementById(trigger.getAttribute("data-panel"));
                if (panel) {
                    // Only now does the #anchor stop being the right answer.
                    e.preventDefault();
                    showPanel(panel, trigger);
                }
                return;
            }

            if (e.target.closest && e.target.closest("[data-panel-close]")) {
                closePanel();
                return;
            }

            if (openPanel && e.target === scrim) {
                closePanel();
            }
        });

        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && openPanel) {
                closePanel();
            }
        });
    }

    /*
        Copy to clipboard.

        The thing being copied is ALWAYS in the page in a box somebody can
        select by hand - this only saves the selecting. With the script blocked,
        or on a browser that refuses clipboard access, select-all inside the box
        still works, which is why the button is an addition to that and not a
        replacement for it.
    */
    document.addEventListener("click", function (e) {
        var button = e.target.closest ? e.target.closest("[data-copy]") : null;
        if (!button) {
            return;
        }

        var source = document.getElementById(button.getAttribute("data-copy"));
        if (!source) {
            return;
        }

        var say = function (text) {
            var original = button.innerHTML;
            button.textContent = text;
            window.setTimeout(function () { button.innerHTML = original; }, 1600);
        };

        // Falling back means asking the person to press Ctrl+C, which only
        // makes sense if they can see what they would be copying. The box may
        // be inside a collapsed <details>, so open it first - a hidden element
        // cannot take focus or hold a selection either.
        var fallback = function () {
            var box = source.closest ? source.closest("details") : null;
            if (box) {
                box.open = true;
            }

            source.focus();
            source.select();
            say("Press Ctrl+C");
        };

        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(source.value).then(
                function () { say(button.getAttribute("data-copied") || "Copied"); },
                fallback);
            return;
        }

        fallback();
    });

    var busyButtons = document.querySelectorAll("[data-busy]");

    for (var i = 0; i < busyButtons.length; i++) {
        busyButtons[i].addEventListener("click", function (e) {
            var button = e.currentTarget;
            var form = button.form;

            // Nothing to acknowledge if the browser is about to reject the form.
            if (form && form.checkValidity && !form.checkValidity()) {
                return;
            }

            window.setTimeout(function () {
                // Keep the label, so a submit that is cancelled after this
                // point can put the button back. Without it a prevented submit
                // leaves a disabled button reading "Saving…" until the page is
                // reloaded by hand.
                if (!button.hasAttribute("data-busy-was")) {
                    button.setAttribute("data-busy-was", button.textContent);
                }

                button.textContent = button.getAttribute("data-busy");
                button.disabled = true;
                button.classList.add("btn--busy");
            }, 0);
        });
    }

    /*
        Put every busy button back the way it was.

        Called when something cancels a submit after the click has already been
        acknowledged - the blank-price question on the price grid is the first
        such thing, and will not be the last.
    */
    window.stRestoreBusy = function (root) {
        var scope = root || document;
        var busy = scope.querySelectorAll("[data-busy-was]");

        for (var b = 0; b < busy.length; b++) {
            busy[b].textContent = busy[b].getAttribute("data-busy-was");
            busy[b].removeAttribute("data-busy-was");
            busy[b].disabled = false;
            busy[b].classList.remove("btn--busy");
        }
    };
})();
