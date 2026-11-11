const downloadButton = document.querySelector("#download-button");
const releaseVersion = document.querySelector("#release-version");
const releaseMeta = document.querySelector("#release-meta");
const releaseHash = document.querySelector("#release-sha256");
const copyButton = document.querySelector("#copy-checksum");
const releaseLink = document.querySelector("#releases-link");
const sourceLink = document.querySelector("#source-link");
const footerSourceLink = document.querySelector("#footer-source-link");
const menuToggle = document.querySelector("#menu-toggle");
const siteNav = document.querySelector("#site-nav");

document.querySelector("#current-year").textContent = String(new Date().getFullYear());

menuToggle.addEventListener("click", () => {
  const isOpen = menuToggle.getAttribute("aria-expanded") === "true";
  menuToggle.setAttribute("aria-expanded", String(!isOpen));
  siteNav.classList.toggle("is-open", !isOpen);
});

siteNav.addEventListener("click", (event) => {
  if (event.target.closest("a")) {
    menuToggle.setAttribute("aria-expanded", "false");
    siteNav.classList.remove("is-open");
  }
});

copyButton.addEventListener("click", async () => {
  const value = releaseHash.textContent.trim();
  if (!value || value === "Disponible avec une version publiée") return;
  try {
    await navigator.clipboard.writeText(value);
    copyButton.textContent = "Copié";
    window.setTimeout(() => { copyButton.textContent = "Copier"; }, 1600);
  } catch {
    copyButton.textContent = "Copie indisponible";
  }
});

async function loadLatestRelease() {
  try {
    const configResponse = await fetch(new URL("./site-config.json", window.location.href), { cache: "no-store" });
    if (!configResponse.ok) throw new Error("Configuration GitHub absente");
    const config = await configResponse.json();
    const repository = typeof config.repository === "string" ? config.repository.trim() : "";
    if (!/^[\w.-]+\/[\w.-]+$/.test(repository)) {
      releaseVersion.textContent = "Téléchargement non configuré";
      releaseMeta.textContent = "Aucun dépôt GitHub n’est configuré pour le site.";
      downloadButton.textContent = "Release non disponible";
      releaseLink.setAttribute("aria-disabled", "true");
      sourceLink.setAttribute("aria-disabled", "true");
      footerSourceLink.setAttribute("aria-disabled", "true");
      return;
    }

    const repositoryUrl = `https://github.com/${repository}`;
    releaseLink.href = `${repositoryUrl}/releases`;
    releaseLink.removeAttribute("aria-disabled");
    sourceLink.href = repositoryUrl;
    sourceLink.removeAttribute("aria-disabled");
    footerSourceLink.href = repositoryUrl;
    footerSourceLink.removeAttribute("aria-disabled");

    const response = await fetch(`https://api.github.com/repos/${repository}/releases/latest`, {
      headers: { Accept: "application/vnd.github+json" }
    });
    if (response.status === 404) {
      releaseVersion.textContent = "Aucune version stable publiée";
      releaseMeta.textContent = "Les releases de test ne sont pas proposées au téléchargement.";
      downloadButton.textContent = "Release non disponible";
      return;
    }
    if (!response.ok) throw new Error(`GitHub Releases a répondu ${response.status}`);

    const release = await response.json();
    const asset = release.assets.find((item) => item.name === "NORADRENALINE-ULTRA-Windows-x64.zip");
    const checksumAsset = release.assets.find((item) => item.name === "NORADRENALINE-ULTRA-Windows-x64.zip.sha256");
    if (!asset || !asset.browser_download_url) {
      releaseVersion.textContent = release.tag_name || "Release incomplète";
      releaseMeta.textContent = "Le paquet Windows x64 n’est pas attaché à cette release.";
      downloadButton.textContent = "Paquet indisponible";
      return;
    }

    releaseVersion.textContent = release.name || release.tag_name || "Version stable";
    const publishedAt = release.published_at
      ? new Intl.DateTimeFormat("fr-FR", { dateStyle: "medium" }).format(new Date(release.published_at))
      : "version publiée";
    const sizeMb = (asset.size / (1024 * 1024)).toFixed(1);
    releaseMeta.textContent = `${publishedAt} · ${sizeMb} Mo · Windows x64`;
    downloadButton.href = asset.browser_download_url;
    downloadButton.download = asset.name;
    downloadButton.classList.remove("is-disabled");
    downloadButton.removeAttribute("aria-disabled");
    downloadButton.innerHTML = 'Télécharger <span aria-hidden="true">↓</span>';

    let digest = typeof asset.digest === "string" && asset.digest.startsWith("sha256:")
      ? asset.digest.slice("sha256:".length)
      : "";
    if (!digest && checksumAsset?.browser_download_url) {
      const checksumResponse = await fetch(checksumAsset.browser_download_url);
      if (checksumResponse.ok) {
        const checksumText = await checksumResponse.text();
        digest = checksumText.trim().split(/\s+/)[0] || "";
      }
    }
    if (/^[a-f0-9]{64}$/i.test(digest)) {
      releaseHash.textContent = digest.toLowerCase();
      copyButton.disabled = false;
    }
  } catch {
    releaseVersion.textContent = "Téléchargement non configuré";
    releaseMeta.textContent = "Aucune source GitHub n’est configurée ou la vérification est indisponible.";
    downloadButton.textContent = "Release non disponible";
    downloadButton.classList.add("is-disabled");
    downloadButton.setAttribute("aria-disabled", "true");
  }
}

loadLatestRelease();
