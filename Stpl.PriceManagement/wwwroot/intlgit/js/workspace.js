/*
    Price revision workspace — progressive enhancement.

    Everything here works without JavaScript: each section is a form that posts
    and re-renders. What this adds is the immediate feedback a dense grid needs.

    THE ONE RULE THIS FILE OBEYS
    It never calculates a selling price. The pricing formula is defined once, in
    intlgit.fn_PriceMatrix, and reimplementing it here is how a screen starts
    quietly disagreeing with its database. When a fare changes, the page ASKS
    the server what the prices would be and paints the answer.

    The cost-per-person totals below ARE calculated here, deliberately: they are
    a sum of figures already on the page, not a pricing rule, and they are what
    makes an FX change legible while you type it.
*/
(function () {
    "use strict";

    var indian = new Intl.NumberFormat("en-IN", { maximumFractionDigits: 0 });

    /*
        The address of this screen. Normally the current path; but when a save
        is refused and the page is drawn in place, the browser is sitting on the
        save's own address (/IntlGit/Revision/HubGrid) - so the page says which
        screen it is, and everything below keys on that.
    */
    var pageScript = document.querySelector("script[data-page-path]");
    var pagePath = (pageScript && pageScript.getAttribute("data-page-path")) || location.pathname;

    /*
        ---- Staying where you were ----------------------------------------

        Expanding a hub, collapsing one, toggling edit mode and every save are
        all full page loads: the server renders the grid only for hubs that are
        open, so the browser has to fetch the page again.

        That used to land you at the top. The answer was to put a #hub-xxx on
        the link, which landed you at the hub - with the hub's header pinned to
        the top of the window and everything you had been looking at pushed off
        it. Better than the top of the page, and still not where you were.

        So: remember the scroll offset on the way out, put it straight back on
        the way in. The page does not move at all.

        The stored value is cleared as soon as it is used, which is what stops a
        fresh arrival from the products list inheriting last week's position.
        Only navigations that stay on this screen record anything, so coming
        back from another page does not restore either.

        With the script blocked the #hub-xxx anchor is still on every link and
        still does its older, coarser job.
    */
    var scrollStore = (function () {
        var key = null;

        var code = new URLSearchParams(location.search).get("code");
        if (code) {
            key = "st:scroll:" + pagePath + ":" + code;
        }

        // Private browsing and blocked site data both throw on access rather
        // than returning null, and losing a scroll position is not worth an
        // exception that stops the rest of this file running.
        function safely(fn) {
            try {
                return fn();
            } catch (e) {
                return null;
            }
        }

        return {
            remember: function () {
                if (!key) { return; }
                safely(function () {
                    sessionStorage.setItem(key, String(window.scrollY));
                });
            },
            take: function () {
                if (!key) { return null; }
                return safely(function () {
                    var value = sessionStorage.getItem(key);
                    sessionStorage.removeItem(key);
                    return value === null ? null : parseInt(value, 10);
                });
            }
        };
    })();

    if (window.history && "scrollRestoration" in window.history) {
        // Otherwise the browser restores its own idea of the position on
        // back/forward, a beat after we have restored ours.
        window.history.scrollRestoration = "manual";
    }

    // Record on the way out, but only for navigations that come back here.
    document.addEventListener("click", function (e) {
        var link = e.target.closest ? e.target.closest("a[href]") : null;
        if (link && link.pathname === pagePath) {
            scrollStore.remember();
        }
    }, true);

    document.addEventListener("submit", function (e) {
        var form = e.target;
        // A form with no action posts to the current URL, which is this page.
        if (!form.action || form.action.indexOf(pagePath) !== -1) {
            scrollStore.remember();
        }
    }, true);

    // A submit button can override its form's action - that is how the grid's
    // withdraw and confirm buttons work - so they are caught on click too.
    document.addEventListener("click", function (e) {
        var button = e.target.closest ? e.target.closest("[formaction]") : null;
        if (button && button.getAttribute("formaction").indexOf(pagePath) !== -1) {
            scrollStore.remember();
        }
    }, true);

    var restoreTo = scrollStore.take();

    if (restoreTo !== null && !isNaN(restoreTo)) {
        var restore = function () {
            window.scrollTo(0, restoreTo);
        };

        // After the browser's own jump to the fragment, which happens as the
        // document is parsed. A second frame covers the layout settling once
        // the web font swaps in.
        requestAnimationFrame(function () {
            restore();
            requestAnimationFrame(restore);
        });

        window.addEventListener("load", restore);
    }

    /*
        A box that may be empty. Returns null for "nobody has entered this",
        and a number only where there is one.

        It used to return 0 for both, which drew a cost per person built out of
        blanks - the 85,600-against-340,000 mistake, rendered live on screen
        while somebody is still typing, and looking exactly like a real total.
        The server says nothing for the same cells, so a zero here also made
        the page disagree with what it had just been sent.
    */
    function read(input) {
        if (!input) {
            return null;
        }

        var raw = String(input.value).replace(/,/g, "").trim();
        if (raw === "") {
            return null;
        }

        var value = parseFloat(raw);
        return isNaN(value) ? null : value;
    }

    // ---- "same as calculated SP" helpers ------------------------------------
    // Function declarations, so they are available to the fare preview's
    // paint() below as well as to the listener at the end of this file.

    // Price[hub|date|occupancy] -> hub
    function hubOfPriceName(name) {
        var inner = name.substring(6, name.length - 1);
        return inner.split("|")[0];
    }

    function syncSame(hub) {
        if (!hub) {
            return;
        }

        var switches = document.querySelectorAll('[data-same-hub="' + hub + '"]');
        if (switches.length === 0) {
            return;
        }

        var boxes = document.querySelectorAll('input[name^="Price[' + hub + '|"]');
        if (boxes.length === 0) {
            // Collapsed hub: nothing on the page to judge by, so leave the
            // server's answer alone.
            return;
        }

        var allMatch = true;

        for (var i = 0; i < boxes.length; i++) {
            var calcRaw = boxes[i].getAttribute("data-calc");
            var value = read(boxes[i]);
            var calc = calcRaw === null || calcRaw === "" ? null : parseFloat(calcRaw);

            if (value === null || calc === null || isNaN(calc)
                || Math.round(value) !== Math.round(calc)) {
                allMatch = false;
                break;
            }
        }

        for (var s = 0; s < switches.length; s++) {
            var sw = switches[s];
            var isButton = sw.tagName === "BUTTON";
            var ticked = allMatch;

            sw.setAttribute("aria-checked", ticked ? "true" : "false");

            var use = sw.querySelector("use");
            if (use) {
                use.setAttribute("href", ticked ? "#i-square-check" : "#i-square");
            }

            if (!isButton) {
                sw.setAttribute("title", ticked
                    ? "Every price on this hub is the calculated one"
                    : "Some prices on this hub were typed by hand");
            }
        }
    }

    // ---- Cost build totals -------------------------------------------------

    var fx = document.getElementById("fx");
    var slab = document.getElementById("slab");
    var shared = document.getElementById("shared");

    if (fx && slab && shared) {
        var recalculateTotals = function () {
            var fxRate = read(fx);
            var paxSlab = read(slab);
            var sharedCost = read(shared);

            /* A share nobody can work out yet is unknown, not nought - whether
               because no shared cost has been entered or because there is no
               slab to divide it by. Mirrors the server, which returns null for
               the same two cases. */
            var sharePerPerson = (sharedCost === null || paxSlab === null || paxSlab <= 0)
                ? null
                : sharedCost / paxSlab;

            var cells = document.querySelectorAll("[data-total]");

            for (var i = 0; i < cells.length; i++) {
                var occupancy = cells[i].getAttribute("data-total");

                var land = document.querySelector(
                    '.js-cost[data-kind="land"][data-occupancy="' + occupancy + '"]');
                var perPerson = document.querySelector(
                    '.js-cost[data-kind="perperson"][data-occupancy="' + occupancy + '"]');

                var landValue = read(land);
                var perPersonValue = read(perPerson);

                // Every part has to be there. One missing makes the total
                // unknown, and an unknown total shows as nothing at all.
                var known = landValue !== null && fxRate !== null
                         && perPersonValue !== null && sharePerPerson !== null;

                cells[i].textContent = known
                    ? indian.format(Math.round(landValue * fxRate + perPersonValue + sharePerPerson))
                    : "";
            }
        };

        var watched = [fx, slab, shared].concat(
            Array.prototype.slice.call(document.querySelectorAll(".js-cost")));

        for (var w = 0; w < watched.length; w++) {
            watched[w].addEventListener("input", recalculateTotals);
        }
    }

    // ---- Live prices as fares are typed ------------------------------------

    var fareInputs = document.querySelectorAll(".js-fare");
    var settings = document.querySelector("script[data-preview-url]");

    if (fareInputs.length > 0 && settings) {
        var previewUrl = settings.getAttribute("data-preview-url");
        var priceEditable = settings.getAttribute("data-price-editable") === "true";
        var token = document.querySelector('input[name="__RequestVerificationToken"]');

        var fareKey = function (input) {
            return input.getAttribute("data-hub") + "|" + input.getAttribute("data-date");
        };

        /*
            Which price boxes somebody has typed into and not yet saved.

            The preview repaints prices whenever a fare changes, and a fare
            changing is exactly what unconfirms the prices built on it - so the
            answer comes back "no agreed price" for the whole row, and paint()
            used to empty every box in it. Type six prices, correct one fare on
            that row, lose all six. Silently, with no way to get them back.

            Marked here by delegation rather than by binding each box, because
            paint() creates boxes of its own for cells that had no fare a moment
            ago, and those need the same protection from the next repaint.

            On DOCUMENT, and it has to be. #faregrid is a nearly empty <form>
            holding three hidden inputs; every price and fare box lives outside
            it in the markup and is tied to it by the form="faregrid" attribute
            instead, so the grid can be laid out as hub blocks rather than as
            one enormous form element. A listener on the form therefore never
            sees a price box's events - the first version of this fix put it
            there and silently did nothing, which only showed up on a real page.

            The mark lives until the page reloads. Submitting the grid IS the
            save, and what comes back is then the stored figure.
        */
        document.addEventListener("input", function (e) {
            var el = e.target;
            if (el && el.name && el.name.indexOf("Price[") === 0) {
                el.setAttribute("data-typed", "1");
            }
        });

        // Every band for one departure, because a price needs all three.
        var faresFor = function (key) {
            var out = [];
            for (var i = 0; i < fareInputs.length; i++) {
                if (fareKey(fareInputs[i]) !== key) {
                    continue;
                }

                var raw = String(fareInputs[i].value).replace(/,/g, "").trim();
                if (raw === "") {
                    continue;
                }

                var amount = parseFloat(raw);
                if (isNaN(amount)) {
                    continue;
                }

                out.push({
                    hub: fareInputs[i].getAttribute("data-hub"),
                    date: fareInputs[i].getAttribute("data-date"),
                    band: fareInputs[i].getAttribute("data-band"),
                    amount: amount
                });
            }

            return out;
        };

        // A cell that showed "awaiting airfare" has no figures in it at all, so
        // painting sometimes means building the parts rather than updating them.
        var part = function (target, className, tag) {
            var existing = target.querySelector("." + className);
            if (existing) {
                return existing;
            }

            var made = document.createElement(tag || "span");
            made.className = className;
            target.appendChild(made);
            return made;
        };

        /*
            A figure that may not be there. indian.format(null) is "0", because
            Number(null) is 0 - so every absent price would render as a real,
            wrong amount rather than as nothing. Anywhere a price is optional,
            this is what formats it.
        */
        var money = function (value) {
            return value === null || value === undefined ? "" : indian.format(value);
        };

        var paint = function (cells) {
            for (var i = 0; i < cells.length; i++) {
                var cell = cells[i];
                var date = String(cell.date).substring(0, 10);
                var key = cell.hub + "|" + date + "|" + cell.occupancy;
                var target = document.querySelector('[data-cell="' + key + '"]');

                if (!target || !cell.hasFare) {
                    continue;
                }

                var awaiting = target.querySelector(".faregrid__awaiting");
                if (awaiting) {
                    awaiting.parentNode.removeChild(awaiting);
                }

                part(target, "faregrid__calc").textContent =
                    "calc " + indian.format(cell.calculated);

                if (priceEditable) {
                    var input = target.querySelector('input[name^="Price["]');

                    if (!input) {
                        // A cell that had no fare a moment ago has no
                        // confirmation either - nobody can have agreed to a
                        // price that did not exist - so it is born a ghost.
                        input = document.createElement("input");
                        input.className =
                            "field field--edit field--num field--price field--fill field--ghost";
                        input.name = "Price[" + key + "]";
                        input.setAttribute("inputmode", "decimal");
                        target.appendChild(input);
                        target.classList.add("faregrid__price--unconfirmed");
                    }

                    // What "same as calculated SP" compares this box with.
                    input.setAttribute("data-calc", cell.calculated);

                    /*
                        A cell with no agreed price is deliberately EMPTY, and
                        empty all the way down - no value and no placeholder.

                        Filling it in here would agree to the figure on the
                        person's behalf, on the very keystroke that emptied it,
                        since typing a fare is what unconfirms the prices built
                        on it. The gate would be defeated by the interaction it
                        exists for.

                        It also cannot be formatted: published is null here, and
                        indian.format(null) is "0" - a plausible, wrong number
                        in a box somebody is about to agree to.
                    */
                    var hasPrice = cell.published !== null && cell.published !== undefined;

                    // The cell's confirmation state is a fact about the data and
                    // is marked whatever is in the box.
                    if (!hasPrice) {
                        target.classList.add("faregrid__price--unconfirmed");
                    }

                    /*
                        A box somebody is typing in is not ours to touch, by
                        EITHER route.

                        The repaint is set off by their own keystroke on a fare,
                        and both branches below answered it by destroying what
                        they had typed on that row - the null branch by emptying
                        the box, and the other by replacing the figure with a
                        freshly calculated one. The second is the worse of the
                        two, because a box that still holds a plausible number
                        does not look like it has lost anything.

                        isOverridden protected a SAVED override and had nothing
                        to say about one still being typed. This is the same
                        principle, applied a step earlier: never overwrite a
                        figure a person put there.
                    */
                    if (input.getAttribute("data-typed") === "1") {
                        // Leave it exactly as they left it.
                    } else if (!hasPrice) {
                        /*
                            A cell with no agreed price is deliberately EMPTY,
                            and empty all the way down - no value and no
                            placeholder.

                            Filling it in here would agree to the figure on the
                            person's behalf, on the very keystroke that emptied
                            it, since typing a fare is what unconfirms the prices
                            built on it. The gate would be defeated by the
                            interaction it exists for.

                            It also cannot be formatted: published is null here,
                            and indian.format(null) is "0" - a plausible, wrong
                            number in a box somebody is about to agree to.
                        */
                        input.value = "";
                        input.removeAttribute("placeholder");
                        input.classList.add("field--ghost");
                    } else if (!cell.isOverridden) {
                        // Never overwrite a figure somebody has typed over: an
                        // override is a deliberate price, not a derived one.
                        input.value = indian.format(cell.published);
                        input.classList.remove("field--ghost");
                    }
                } else {
                    part(target, "faregrid__pub").textContent =
                        money(cell.published);
                }

                /*
                    The struck-through figure, and whether it is still
                    provisional. It follows the CALCULATED price while the box
                    is empty, so the marking has to be repainted with it - a
                    preview that firmed up the figure without firming up the
                    figure's meaning would be worse than not repainting at all.
                */
                var mrp = part(target, "faregrid__mrp");
                mrp.textContent = money(cell.strikeThrough);
                mrp.classList.toggle(
                    "faregrid__mrp--provisional",
                    cell.published === null || cell.published === undefined);

                // What is on the website now, which does not move with a
                // preview - but the element may not exist yet on a cell that
                // has just gained a fare.
                if (cell.livePrice !== null && cell.livePrice !== undefined) {
                    part(target, "faregrid__lastpub").textContent =
                        "last published " + indian.format(cell.livePrice);
                }

                // The gap only exists while an override is in force, and it is
                // measured against the price this preview just changed - so it
                // has to be redrawn, or removed, every time.
                var delta = target.querySelector(".faregrid__delta");
                var gap = cell.published === null || cell.published === undefined
                    ? 0
                    : Math.round(cell.published) - Math.round(cell.calculated);

                if (cell.isOverridden && gap !== 0) {
                    if (!delta) {
                        delta = part(target, "faregrid__delta");
                    }

                    delta.textContent =
                        (gap > 0 ? "+" : "") + indian.format(gap) + " vs calc";
                } else if (delta) {
                    delta.parentNode.removeChild(delta);
                }

                target.classList.add("faregrid__price--preview");
            }

            // A fare moving changes the calculated figures under the boxes,
            // so the hub switch has to be re-read too.
            var seenHubs = {};
            for (var h = 0; h < cells.length; h++) {
                if (!seenHubs[cells[h].hub]) {
                    seenHubs[cells[h].hub] = true;
                    syncSame(cells[h].hub);
                }
            }
        };

        var pending = null;

        var preview = function (key) {
            var fares = faresFor(key);
            if (fares.length === 0 || !previewUrl) {
                return;
            }

            // One request in flight at a time; a newer edit supersedes an older.
            if (pending) {
                pending.abort();
            }

            pending = new AbortController();

            var headers = { "Content-Type": "application/json" };
            if (token) {
                headers.RequestVerificationToken = token.value;
            }

            fetch(previewUrl, {
                method: "POST",
                headers: headers,
                body: JSON.stringify({ fares: fares }),
                signal: pending.signal
            })
                .then(function (response) {
                    return response.ok ? response.json() : null;
                })
                .then(function (cells) {
                    if (cells) {
                        paint(cells);
                    }
                })
                .catch(function () {
                    // A failed or superseded preview is not worth interrupting
                    // anyone over: the figures on screen are still the saved
                    // ones, and saving recalculates properly either way.
                });
        };

        for (var f = 0; f < fareInputs.length; f++) {
            fareInputs[f].addEventListener("change", function (e) {
                preview(fareKey(e.target));
            });
        }
    }

    // ---- Show changed rows only --------------------------------------------

    var switches = document.querySelectorAll(".js-onlychanged");

    for (var s = 0; s < switches.length; s++) {
        switches[s].addEventListener("change", function (e) {
            var grid = document.getElementById("grid-" + e.target.getAttribute("data-hub"));
            if (grid) {
                grid.classList.toggle("faregrid--onlychanged", e.target.checked);
            }
        });
    }

    // ---- Show past departures ----------------------------------------------
    //
    // Departures that have gone are hidden by CSS, not removed, so this only
    // flips a class. With the script blocked they are simply all visible, which
    // is the honest degradation: nothing is lost, the grid is just longer.

    var pastSwitches = document.querySelectorAll(".js-showpast");

    for (var p = 0; p < pastSwitches.length; p++) {
        pastSwitches[p].addEventListener("change", function (e) {
            var grid = document.getElementById("grid-" + e.target.getAttribute("data-hub"));
            if (grid) {
                grid.classList.toggle("faregrid--showpast", e.target.checked);
            }
        });
    }

    // ---- Unsaved work ------------------------------------------------------

    /*
        One Save now commits every hub on the page, which makes leaving with
        work in progress more expensive than it used to be: collapsing a hub or
        following a link reloads the page and the typing goes with it. So the
        page keeps track of whether anything has been edited and asks first.

        Submitting the grid clears the flag - that IS the save.
    */
    var gridForm = document.getElementById("faregrid");

    if (gridForm) {
        var dirty = false;

        var fields = gridForm.elements;
        for (var d = 0; d < fields.length; d++) {
            fields[d].addEventListener("input", function () {
                dirty = true;
            });
        }

        /*
            Blank published prices: ask before saving, not after.

            Saving them is allowed and always was - the departure is simply held
            out of the change set until somebody prices it. What was missing is
            that nothing said so, so the week's work looked finished when three
            departures were not going anywhere.

            The count comes from the form itself, which is also what the
            server counts on the no-script path, so the two say the same number.
        */
        var blankModal = document.getElementById("blankprices");
        var saveAnyway = false;

        /*
            form.elements, NOT querySelectorAll on the form.

            The price boxes are not inside the <form> element - forms cannot
            nest, and each hub carries its own little forms, so every box in the
            grid is associated by a form="faregrid" attribute instead. Searching
            the form's descendants finds none of them, which reads as "no blanks"
            and asks nothing.
        */
        var countBlankPrices = function () {
            var boxes = gridForm.elements;
            var blank = 0;

            for (var b = 0; b < boxes.length; b++) {
                var name = boxes[b].name || "";

                if (name.indexOf("Price[") === 0 && boxes[b].value.trim() === "") {
                    blank++;
                }
            }

            return blank;
        };

        var closeBlankModal = function () {
            blankModal.hidden = true;
            document.documentElement.classList.remove("panel-open");

            // The click was already acknowledged by the busy handler, so the
            // button is sitting disabled and reading "Saving…" for a save that
            // is not happening.
            if (window.stRestoreBusy) {
                window.stRestoreBusy(document);
            }
        };

        gridForm.addEventListener("submit", function (e) {
            dirty = false;

            if (!blankModal || saveAnyway) {
                return;
            }

            var blank = countBlankPrices();
            if (blank === 0) {
                return;
            }

            e.preventDefault();
            dirty = true;

            var count = blankModal.querySelector("[data-blank-count]");
            if (count) {
                count.textContent = indian.format(blank);
            }

            blankModal.hidden = false;
            document.documentElement.classList.add("panel-open");

            var cancel = blankModal.querySelector("[data-blank-cancel]");
            if (cancel) {
                cancel.focus();
            }
        });

        if (blankModal) {
            blankModal.addEventListener("click", function (e) {
                if (e.target.closest && e.target.closest("[data-blank-cancel]")) {
                    closeBlankModal();
                    return;
                }

                if (e.target.closest && e.target.closest("[data-blank-confirm]")) {
                    // The next submit is the real one.
                    saveAnyway = true;
                    blankModal.hidden = true;
                }
            });

            document.addEventListener("keydown", function (e) {
                if (e.key === "Escape" && !blankModal.hidden) {
                    closeBlankModal();
                }
            });
        }

        window.addEventListener("beforeunload", function (e) {
            if (!dirty) {
                return undefined;
            }

            // The browser shows its own wording; returning a string is what
            // asks it to prompt at all.
            e.preventDefault();
            e.returnValue = "";
            return "";
        });

        /*
            Save first, then do what was asked.

            Every other action on this page - a hub's markup box, "same as
            calculated SP", adding a date, withdrawing, expanding a hub - is its
            own form or link, and reloads the page. With a fare or price typed
            and not yet saved, that used to stop on the browser's "leave site?"
            question, so the only way on was to press Save all hubs by hand
            first. Typing the tour manager's fare on Joining / Leaving and then
            setting its markup or ticking "same as calculated SP" hit exactly
            that.

            Now the grid is saved in the background first, and the action goes
            ahead straight after. If the save is refused (somebody else moved
            the tour), the grid is submitted normally instead, so the page shows
            why - nothing typed is lost either way.
        */
        var saving = false;

        var saveGridThen = function (proceed) {
            if (saving) {
                return;
            }

            saving = true;

            var data = new FormData(gridForm);

            fetch(gridForm.action, {
                method: "POST",
                body: data,
                credentials: "same-origin",
                redirect: "follow"
            })
                .then(function (response) {
                    // Success is a redirect back to the page. A refusal renders
                    // the page in place (200, no redirect) with the reason on it.
                    if (response.ok && response.redirected) {
                        dirty = false;
                        proceed();
                        return;
                    }

                    throw new Error("save refused");
                })
                .catch(function () {
                    saving = false;
                    if (window.stRestoreBusy) {
                        window.stRestoreBusy(document);
                    }

                    // Let the normal save show what went wrong.
                    saveAnyway = true;
                    dirty = false;
                    gridForm.submit();
                });
        };

        document.addEventListener("submit", function (e) {
            var form = e.target;

            if (!dirty || !form || form === gridForm
                || String(form.method).toLowerCase() !== "post") {
                return;
            }

            e.preventDefault();
            saveGridThen(function () {
                // Native submit: fires no submit event, so this does not loop.
                form.submit();
            });
        }, true);

        document.addEventListener("click", function (e) {
            if (!dirty || e.defaultPrevented || e.button !== 0
                || e.metaKey || e.ctrlKey || e.shiftKey) {
                return;
            }

            var link = e.target.closest ? e.target.closest("a[href]") : null;
            if (!link || link.target === "_blank") {
                return;
            }

            var href = link.getAttribute("href");
            if (!href || href.charAt(0) === "#" || link.hasAttribute("data-panel")) {
                return;
            }

            e.preventDefault();
            saveGridThen(function () {
                window.location.href = link.href;
            });
        });
    }

    // ---- "same as calculated SP" follows the boxes -------------------------

    /*
        The switch on each hub header says "every price on this hub is the
        calculated one". The server works that out when the page is drawn, so
        after pressing it every box is filled and it reads ticked - and it
        stayed ticked while somebody typed a different figure into one of the
        boxes, right up until they saved. It now unticks the moment a box
        stops matching its calculated price, and ticks again if every box is
        put back.

        Pressing it (the button form) fills the empty boxes; the tick itself
        only ever says whether the boxes match.
    */
    document.addEventListener("input", function (e) {
        var el = e.target;
        if (el && el.name && el.name.indexOf("Price[") === 0) {
            syncSame(hubOfPriceName(el.name));
        }
    });

    // Buttons that acknowledge a click live in site.js - every screen has them,
    // not just this one.

    // ---- Add hub panel -----------------------------------------------------

    var addHub = document.querySelector(".js-addhub");
    if (addHub) {
        var card = addHub.closest(".card");
        var form = card ? card.querySelector(".inline-form") : null;

        if (form) {
            form.hidden = !addHub.open;
            addHub.addEventListener("toggle", function () {
                form.hidden = !addHub.open;
            });
        }
    }
})();
