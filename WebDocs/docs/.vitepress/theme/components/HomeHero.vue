<script setup lang="ts">
import { onMounted, ref } from 'vue'
const mounted = ref(false)
onMounted(() => (mounted.value = true))
</script>

<template>
  <section class="hero" :class="{ on: mounted }">
    <div class="hero-bg" aria-hidden="true">
      <svg viewBox="0 0 1200 520" preserveAspectRatio="xMidYMid slice">
        <defs>
          <radialGradient id="glow" cx="50%" cy="60%" r="60%">
            <stop offset="0" stop-color="#ebdc9e" stop-opacity="0.22" />
            <stop offset="0.5" stop-color="#a7853b" stop-opacity="0.08" />
            <stop offset="1" stop-color="#0b0b0d" stop-opacity="0" />
          </radialGradient>
          <linearGradient id="horizon" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0" stop-color="#ebdc9e" stop-opacity="0" />
            <stop offset="0.5" stop-color="#ebdc9e" stop-opacity="0.9" />
            <stop offset="1" stop-color="#ebdc9e" stop-opacity="0" />
          </linearGradient>
        </defs>
        <rect width="1200" height="520" fill="url(#glow)" />
        <g class="waves" stroke="#c9ced6" stroke-opacity="0.12" fill="none" stroke-width="1">
          <path v-for="i in 9" :key="i" :d="`M-50 ${330 + i * 18} C 250 ${300 + i * 18}, 450 ${360 + i * 18}, 700 ${330 + i * 18} S 1100 ${300 + i * 18}, 1250 ${330 + i * 18}`" :style="{ animationDelay: `${i * -0.7}s` }" />
        </g>
        <rect x="0" y="318" width="1200" height="2" fill="url(#horizon)" opacity="0.35" />
      </svg>
    </div>
    <!-- Moon over the sea, ported from the original argentsea.com landing page (ArgentSea.Home): a fixed
         680x400 photo flanked by SVG horizon strips that stretch to the edges and fade out. -->
    <div class="moon-scene" aria-hidden="true">
      <div class="horizon">
        <div class="horizon-side">
          <svg xmlns="http://www.w3.org/2000/svg" height="400" width="100%" preserveAspectRatio="none">
            <defs>
              <linearGradient id="horizonL" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0" stop-color="#fff" stop-opacity="0.3" />
                <stop offset="1" stop-color="#fff" stop-opacity="0" />
              </linearGradient>
              <linearGradient id="fadeL">
                <stop offset="0.1" stop-color="#000000" />
                <stop offset="0.9" stop-color="#000000" stop-opacity="0" />
              </linearGradient>
            </defs>
            <rect y="246" width="100%" height="40" fill="url(#horizonL)" />
            <rect width="100%" height="400" fill="url(#fadeL)" />
          </svg>
        </div>
        <div class="horizon-center">
          <img src="/images/argentsea-home.jpg" alt="" width="680" height="400" />
          <svg xmlns="http://www.w3.org/2000/svg" height="400" width="680">
            <defs>
              <linearGradient id="horizonC" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0" stop-color="#fff" stop-opacity="0.3" />
                <stop offset="1" stop-color="#fff" stop-opacity="0" />
              </linearGradient>
            </defs>
            <rect y="246" width="680" height="40" fill="url(#horizonC)" />
          </svg>
        </div>
        <div class="horizon-side">
          <svg xmlns="http://www.w3.org/2000/svg" height="400" width="100%" preserveAspectRatio="none">
            <defs>
              <linearGradient id="horizonR" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0" stop-color="#fff" stop-opacity="0.3" />
                <stop offset="1" stop-color="#fff" stop-opacity="0" />
              </linearGradient>
              <linearGradient id="fadeR">
                <stop offset="0.1" stop-color="#000000" stop-opacity="0" />
                <stop offset="0.9" stop-color="#000000" />
              </linearGradient>
            </defs>
            <rect y="246" width="100%" height="40" fill="url(#horizonR)" />
            <rect width="100%" height="400" fill="url(#fadeR)" />
          </svg>
        </div>
      </div>
    </div>
    <div class="hero-inner">
      <img class="hero-logo" src="/images/argentsealogo-gold.svg" alt="ArgentSea" />
      <p class="hero-tag">
        A .NET data-access framework built for <span>simplicity</span>, <span>high performance</span> and
        <span>global scale</span>.
      </p>
      <p class="hero-sub">
        Shard a large data set across servers. Give every tenant a database of their own. Split reads from writes.
        Map results at hand-written ADO.NET speed. Persist Microsoft Orleans grains in real relational tables.
        ArgentSea is the only .NET framework designed for scaling data out, and it is a pleasure to use even
        when you never shard.
      </p>
      <div class="hero-actions">
        <a class="btn primary" href="/getting-started/installation">Get started</a>
        <a class="btn ghost" href="/introduction/what-is-argentsea">What is ArgentSea?</a>
        <a class="btn ghost" href="https://github.com/argentsea/source" target="_blank" rel="noreferrer">GitHub</a>
      </div>
      <div class="hero-badges">
        <span>MIT licensed</span><span>.NET 10</span><span>SQL Server</span><span>PostgreSQL</span><span>Orleans 10</span>
      </div>
    </div>
  </section>
</template>

<style scoped>
.hero {
  /* --pad-top is the hero's top padding; --scene-scale shrinks the moon scene on narrow screens. The scene is
     placed so its horizon (246px down the 400px photo) sits just below the logo, whose width is min(520px, 80vw)
     and whose aspect ratio is 239.16 / 64.27. */
  --pad-top: 88px;
  --scene-scale: 1;
  position: relative;
  overflow: hidden;
  isolation: isolate;
  background: #0b0b0d;
  color: #e9e9ec;
  border-bottom: 1px solid rgba(235, 220, 158, 0.18);
}
.hero-bg {
  position: absolute;
  inset: 0;
}
.hero-bg svg {
  width: 100%;
  height: 100%;
}
.waves path {
  animation: drift 9s ease-in-out infinite;
}
@keyframes drift {
  0%,
  100% {
    transform: translateX(0);
  }
  50% {
    transform: translateX(-28px);
  }
}
.moon-scene {
  position: absolute;
  left: 50%;
  top: calc(var(--pad-top) + min(520px, 80vw) * 64.27 / 239.16 + 8px - 246px * var(--scene-scale));
  width: calc(100% / var(--scene-scale));
  height: 400px;
  transform: translateX(-50%) scale(var(--scene-scale));
  transform-origin: top center;
  /* The photo and fades are pure black; lighten lets the hero background, glow and waves show through them. */
  mix-blend-mode: lighten;
  /* Soften the lower reflection, which sits behind the paragraph copy; the moon and horizon stay at full strength. */
  -webkit-mask-image: linear-gradient(to bottom, #000 0, #000 300px, rgba(0, 0, 0, 0.4) 350px, rgba(0, 0, 0, 0.3) 400px);
  mask-image: linear-gradient(to bottom, #000 0, #000 300px, rgba(0, 0, 0, 0.4) 350px, rgba(0, 0, 0, 0.3) 400px);
  pointer-events: none;
  opacity: 0;
  transition: opacity 1.4s ease;
}
.on .moon-scene {
  opacity: 1;
}
.horizon {
  width: 100%;
  height: 400px;
  display: flex;
  flex-flow: row nowrap;
  justify-content: center;
  overflow: hidden;
}
.horizon-side {
  flex: 1 1 0;
  min-width: 0;
  position: relative;
}
.horizon-side svg {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
}
.horizon-center {
  position: relative;
  flex: 0 0 680px;
  height: 400px;
}
.horizon-center > img,
.horizon-center > svg {
  position: absolute;
  top: 0;
  left: 0;
  max-width: none;
}
.hero-inner {
  position: relative;
  max-width: 980px;
  margin: 0 auto;
  padding: var(--pad-top) 24px 56px;
  text-align: center;
}
.hero-logo {
  width: min(520px, 80vw);
  margin: 0 auto 28px;
  display: block;
  opacity: 0;
  transform: translateY(10px);
  transition: opacity 0.9s ease, transform 0.9s ease;
}
.on .hero-logo {
  opacity: 1;
  transform: none;
}
.hero-tag {
  /* Set explicitly: the site-wide `.vp-doc p` colour is dark in light mode, but the hero is always dark. */
  color: #e9e9ec;
  font-size: clamp(20px, 2.6vw, 30px);
  font-weight: 600;
  letter-spacing: -0.01em;
  margin: 0 0 16px;
  line-height: 1.3;
}
/* A dark halo keeps the copy legible where it crosses the moon's reflection. */
.hero-tag,
.hero-sub {
  text-shadow: 0 0 2px #0b0b0d, 0 0 8px #0b0b0d, 0 0 16px #0b0b0d, 0 0 28px rgba(11, 11, 13, 0.9);
}
.hero-tag span {
  color: #ebdc9e;
}
.hero-sub {
  font-size: 17px;
  line-height: 1.65;
  color: #b9bcc4;
  max-width: 780px;
  margin: 0 auto 30px;
}
.hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  justify-content: center;
}
.btn {
  display: inline-block;
  padding: 11px 22px;
  border-radius: 999px;
  font-weight: 600;
  font-size: 15px;
  text-decoration: none !important;
  transition: transform 0.15s, background 0.15s, border-color 0.15s;
  border: 1px solid transparent;
}
.btn:hover {
  transform: translateY(-1px);
}
.btn.primary {
  background: linear-gradient(120deg, #c9a85a, #ebdc9e);
  color: #141210 !important;
}
.btn.ghost {
  border-color: rgba(201, 206, 214, 0.35);
  color: #e9e9ec !important;
}
.btn.ghost:hover {
  border-color: #ebdc9e;
}
.hero-badges {
  margin-top: 34px;
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  justify-content: center;
}
.hero-badges span {
  font-size: 12px;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: #9aa1ab;
  border: 1px solid rgba(154, 161, 171, 0.3);
  border-radius: 999px;
  padding: 4px 10px;
}
@media (max-width: 640px) {
  .hero {
    --pad-top: 56px;
    --scene-scale: 0.72;
  }
  .hero-inner {
    padding: var(--pad-top) 16px 40px;
  }
}
</style>
