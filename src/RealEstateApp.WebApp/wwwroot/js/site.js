"use strict";

document.querySelectorAll("[data-gallery]").forEach(link => {
    link.addEventListener("click", event => {
        const target = document.getElementById(link.dataset.gallery);
        if (!target) return;
        event.preventDefault();
        target.src = link.href;
        target.alt = link.querySelector("img").alt;
    });
});

document.querySelectorAll("[data-image-preview]").forEach(input => {
    let objectUrl;
    input.addEventListener("change", () => {
        const preview = document.getElementById(input.dataset.imagePreview);
        if (!preview) return;
        if (objectUrl) URL.revokeObjectURL(objectUrl);
        const file = input.files[0];
        const valid = file && ["image/jpeg", "image/png"].includes(file.type) && file.size <= 5 * 1024 * 1024;
        preview.hidden = !valid;
        if (valid) {
            objectUrl = URL.createObjectURL(file);
            preview.src = objectUrl;
        } else {
            preview.removeAttribute("src");
        }
    });
});

document.querySelectorAll(".main-nav a").forEach(link => {
    if (link.pathname === window.location.pathname && link.search === window.location.search) {
        link.setAttribute("aria-current", "page");
    }
});

document.querySelectorAll("[data-open-dialog]").forEach(button => {
    button.addEventListener("click", () => document.getElementById(button.dataset.openDialog)?.showModal());
});

document.querySelectorAll("[data-close-dialog]").forEach(button => {
    button.addEventListener("click", () => button.closest("dialog")?.close());
});

document.querySelectorAll("dialog[data-open-on-load]").forEach(dialog => dialog.showModal());
