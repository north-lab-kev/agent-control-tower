// The stylesheet links are rendered statically so the browser never repaints unstyled.
// Switching the theme at runtime therefore only flips their `media` attributes.
window.actTheme = {
    apply(lightId, lightMedia, darkId, darkMedia) {
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
