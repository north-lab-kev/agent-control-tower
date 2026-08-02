// The stylesheet links are rendered statically so the browser never repaints unstyled.
// Switching the theme at runtime therefore only flips their `media` attributes — and stamps the
// choice on <html>, which is where the `--act-*` tokens and the `--rz-*` overrides written in terms
// of them are keyed from. App.razor writes it for the first paint; this keeps it current.
window.actTheme = {
    apply(theme, lightId, lightMedia, darkId, darkMedia) {
        document.documentElement.dataset.actTheme = theme;

        setMedia(lightId, lightMedia);
        setMedia(darkId, darkMedia);
    },
};

function setMedia(id, media) {
    const link = document.getElementById(id);

    if (link && link.media !== media) {
        link.media = media;
    }
}
