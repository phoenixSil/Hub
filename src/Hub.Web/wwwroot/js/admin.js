// Back-office : conversion d'un visuel SVG en image PNG (dans le navigateur), avant envoi au serveur.
window.hubAdmin = {
  /**
   * Dessine le SVG sur un canevas 1080 × 1080 et renvoie l'image PNG (octets, transmis en flux au serveur :
   * une image dépasse la taille maximale d'un message Blazor).
   */
  svgToPng: function (svg, size) {
    return new Promise(function (resolve, reject) {
      var img = new Image();
      var blob = new Blob([svg], { type: "image/svg+xml;charset=utf-8" });
      var url = URL.createObjectURL(blob);
      img.onload = function () {
        try {
          var canvas = document.createElement("canvas");
          canvas.width = size;
          canvas.height = size;
          var ctx = canvas.getContext("2d");
          ctx.fillStyle = "#ffffff";
          ctx.fillRect(0, 0, size, size);
          ctx.drawImage(img, 0, 0, size, size);
          URL.revokeObjectURL(url);
          canvas.toBlob(function (png) {
            if (!png) { reject("Conversion impossible"); return; }
            png.arrayBuffer().then(function (buffer) { resolve(new Uint8Array(buffer)); }, reject);
          }, "image/png");
        } catch (e) {
          URL.revokeObjectURL(url);
          reject(e && e.message ? e.message : "Conversion impossible");
        }
      };
      img.onerror = function () {
        URL.revokeObjectURL(url);
        reject("Le visuel n'a pas pu être dessiné.");
      };
      img.src = url;
    });
  }
};
