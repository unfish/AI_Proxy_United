<template>
  <MdPreview
      :editorId="mdId"
      :style="{'min-height':height+'px',height:'auto'}"
      :modelValue="content"
  />
</template>

<script setup>
import { MdPreview } from 'md-editor-v3';
import 'md-editor-v3/lib/preview.css';

const props = defineProps({
  height: {
    type: Number,
    default: 60,
  },
  content: {
    type: String,
    default: "",
  },
  mdId: {
    type: String,
    default: "markdown-container",
  },
});
</script>

<style lang="scss" scoped>
</style>