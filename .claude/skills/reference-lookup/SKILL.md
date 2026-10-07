---
name: reference-lookup
description: "Iteratively find visual reference images on the user's personal Pinterest account via Chrome, refining the search with the user's feedback until the references match what they have in mind, then save the confirmed set to a board on their profile. Use when the user asks for a reference, visual reference, mood/inspiration image, or \"what does X look like\" for a design, model, scene, or prop — e.g. \"find me a reference for a rusty cargo crate\", \"look up references for a medieval watchtower\", \"/reference-lookup viking helmet\"."
---

# Reference lookup

This is a conversation, not a one-shot search. The goal is to converge on what the user actually has in mind: search their Pinterest, show 5 candidates, learn from their reaction, search again, and repeat until they confirm the set. Then pin the confirmed set to a board on their profile so it is kept. Treat the user's feedback on images as the most reliable signal of what they want, more than their first description.

## Setup (once per session)

1. **Recall preferences.** Read memory (`memory_list`, then `/topics/reference-lookup.md` if it exists). It holds the user's standing taste for references (e.g. preferred style, level of realism, things they consistently reject) and which Pinterest boards they use for what. Fold these into the first brief silently — do not recite them.
2. **Open Chrome.** Always use Claude in Chrome (the `mcp__claude-in-chrome__*` tools) — the user's real Chrome, where they are logged in to Pinterest. Invoke the `claude-in-chrome` skill, load the core Chrome tools in one ToolSearch call (`tabs_context_mcp`, `navigate`, `computer`, `read_page`, `get_page_text`, `find`, `tabs_create_mcp`, `tabs_close_mcp`), call `tabs_context_mcp`, then create one new tab for Pinterest and reuse it for every round. Do not use the built-in browser unless the user explicitly asks; if Chrome is unavailable, say so and ask before switching. If Pinterest shows a login screen, stop and tell the user it is not signed in — never enter credentials.
3. **Learn the boards.** Open the user's profile and read the list of board names once. Keep it for the session: it drives board-aware searching and the final save.

## The loop

### Round 0 — understand the brief

Start from what the user said plus remembered preferences. Form a quick brief: subject, and any qualifiers (style, era, material, mood, angle, colour, level of realism, intended use — e.g. a 3D model, a scene, a character). If the user named a board ("look in my Props board"), scope the search to it. Do **not** interrogate the user up front; one short clarifying question is allowed only if the subject itself is unclear ("a building"). Otherwise go straight to a first search — images are a better prompt for the user than questions.

### Each round

1. **Search — board-aware.**
   - If a board is in scope (named by the user, remembered as the board for this kind of subject, or whose name obviously matches), open that board and search within it first (`https://www.pinterest.com/<user>/<board-slug>/` and use the board's own search field, or filter the my-pins results by that board).
   - Otherwise search all of the user's saved pins: `https://www.pinterest.com/search/my_pins/?q=<query>` (URL-encoded). Scroll once if fewer than ~10 results load. Note which boards the hits come from; if most come from one board, say so and offer to narrow to it.
   - If fewer than 5 usable matches, fall back to all of Pinterest: `https://www.pinterest.com/search/pins/?q=<query>`, and say which source the results came from.
   - **More like this (round 2 onward).** When the user liked specific pins, open each liked pin and harvest candidates from its "More like this" section before running a new text query — visual similarity converges faster than reworded words. Mix: roughly half from "More like this", half from the refined query.
2. **Gather candidates.** Use `get_page_text` / `read_page` / `find` to collect pin titles, descriptions, boards, and pin URLs (`/pin/<id>/`). Collect ~10–15 candidates, not just 5. Skip duplicates, ads, and off-subject pins. Deliberately include some **variety** in early rounds (different styles/angles/moods) so the user's picks tell you something; tighten variety in later rounds.
3. **Actually look at the images.** Pinterest titles and descriptions are unreliable. Before choosing the 5, screenshot the results grid (scroll and screenshot as needed) and judge each candidate **visually** against the brief: subject correct, style/realism level, material and wear, angle/silhouette clarity, image quality (no watermarks, collages, or tiny thumbnails). Rank by what you see, not by the text. Drop anything that looks right in the caption but wrong in the picture.
4. **Show 5.** For each chosen pin, screenshot it (open the pin or scroll it into view) so the user sees the image, plus a one-line description of what is in it, the board it lives on (if from their pins), and the link. Number them 1–5. If fewer than 5 good matches exist, show what exists and say so — never pad.
5. **Ask for feedback — one question.** Use AskUserQuestion. Ask which of the five are closest to what they mean and what is off. Options along the lines of: *These are right — done* / *Some are close (tell me which)* / *Wrong direction — here's what I mean*. Encourage the user to answer in terms of the images ("more like 2 and 4, less shiny than 1").
6. **Update the brief.** Turn the feedback into concrete changes: add or drop qualifiers, swap synonyms, borrow words from the titles/boards of the liked pins, note what the liked images share visually, exclude the rejected direction. State the updated brief in one line ("Got it — worn, hand-painted wooden crate, low-poly, no metal bands") so the user can correct it.
7. **Repeat** from step 1 with the refined query. Keep pins the user already approved in the set (carry them forward, labelled as kept) and fill the remaining slots with new candidates, so the set grows toward 5 confirmed references rather than restarting.

### Stop conditions

- The user confirms the set is right → go to **Save to a board**.
- After 3 rounds without convergence, pause and ask a more direct question about what is missing (use their own words and the images they liked) rather than continuing to guess.
- If both the user's pins and all of Pinterest return nothing usable for two consecutive queries, say so and ask the user for a different way to describe the subject.

## Save to a board

Once the set is confirmed, pin it to the user's profile:

1. **Choose the board — ask once.** From the board list, propose the likeliest existing boards (by name match, or the board remembered for this kind of subject) or a new board named after the brief (suggest `Ref – <subject>`). Ask one question with AskUserQuestion. If the user already named a board earlier in the conversation, use it without asking.
2. **Save each confirmed pin.** For every pin in the final set, open the pin page, click **Save**, pick the chosen board (create it first if new — keep the user's default visibility, do not change privacy settings), and confirm it was saved. Skip a pin that is already on that board. Do this only for the confirmed pins — never for candidates the user rejected or did not comment on.
3. **Report.** Give the board link, list the pins saved (and any skipped as already present or that failed to save), and the final one-line brief. Then stop.

## Remember preferences

After the set is confirmed (and again if the user states a durable preference mid-search), update `/topics/reference-lookup.md` in memory (read first, then `memory_str_replace`/`memory_append`; create with `memory_write` if absent). File only what the user said or clearly chose, tagged `[stated]`, and keep it short and durable:
- standing taste: styles/realism levels they consistently pick or reject ("prefers stylized low-poly, rejects photoreal renders") — only after it has shown up in their choices more than once, or they said it outright;
- board conventions: which board is for what kind of reference, and the naming pattern they like for new boards;
- one line per subject they've looked up: subject → board it was saved to.
Do not store the pin lists themselves, transient search queries, or anything you inferred but they did not confirm. Never announce the write.

## Rules

- The only write actions on Pinterest are: saving the confirmed pins to the chosen board, and creating that board if the user asked for a new one. Never unsave, delete, like, follow, comment, reorder, merge boards, or change account or privacy settings; never sign in/out.
- Do not save anything before the user has confirmed the set.
- Do not substitute web search or other sites unless the user explicitly asks — the point is their own Pinterest.
- Avoid clicking anything that could open a browser dialog; Pinterest's Save flow uses in-page popovers, which are fine.
- If Chrome is offline, the extension lacks permission for pinterest.com, or Pinterest blocks the page, say so and stop rather than retrying repeatedly.

## Output format per round

One line with the current brief and source (board name / your pins / all of Pinterest), then the 5 numbered results (image, one-line description, board, link, "kept" marker where relevant), then the single feedback question. No long commentary. After saving: board link, saved pins, final brief.