import { h } from 'vue'
import DefaultTheme from 'vitepress/theme'
import type { Theme } from 'vitepress'
import './custom.css'
import PageHeader from './components/PageHeader.vue'
import HomeHero from './components/HomeHero.vue'
import HomeFeatures from './components/HomeFeatures.vue'
import HomeTopology from './components/HomeTopology.vue'
import HomeCodeTeaser from './components/HomeCodeTeaser.vue'
import HomePlatforms from './components/HomePlatforms.vue'
import FanOutDemo from './components/FanOutDemo.vue'
import ConfigHierarchy from './components/ConfigHierarchy.vue'
import ShardKeyAnatomy from './components/ShardKeyAnatomy.vue'
import PackageMap from './components/PackageMap.vue'
import GlossaryIndex from './components/GlossaryIndex.vue'
import ConceptGrid from './components/ConceptGrid.vue'
import QueryPipeline from './components/QueryPipeline.vue'
import RetryTimeline from './components/RetryTimeline.vue'

export default {
  extends: DefaultTheme,
  Layout() {
    return h(DefaultTheme.Layout, null, {
      'doc-before': () => h(PageHeader)
    })
  },
  enhanceApp({ app }) {
    app.component('HomeHero', HomeHero)
    app.component('HomeFeatures', HomeFeatures)
    app.component('HomeTopology', HomeTopology)
    app.component('HomeCodeTeaser', HomeCodeTeaser)
    app.component('HomePlatforms', HomePlatforms)
    app.component('FanOutDemo', FanOutDemo)
    app.component('ConfigHierarchy', ConfigHierarchy)
    app.component('ShardKeyAnatomy', ShardKeyAnatomy)
    app.component('PackageMap', PackageMap)
    app.component('GlossaryIndex', GlossaryIndex)
    app.component('ConceptGrid', ConceptGrid)
    app.component('QueryPipeline', QueryPipeline)
    app.component('RetryTimeline', RetryTimeline)
  }
} satisfies Theme
