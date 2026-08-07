const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('facmPet', {
  onLoadModel(callback) {
    ipcRenderer.on('facm-load-model', (_, payload) => callback(payload));
  },
  readModel(sourcePath) {
    return ipcRenderer.invoke('facm-read-model', sourcePath);
  },
  rendererReady() {
    ipcRenderer.send('facm-renderer-ready');
  },
  modelReady() {
    ipcRenderer.send('facm-model-ready');
  },
  modelError(message) {
    ipcRenderer.send('facm-model-error', String(message || 'VRM load failed'));
  },
  click() {
    ipcRenderer.send('facm-pet-click');
  },
  dragStart() {
    ipcRenderer.send('facm-drag-start');
  },
  dragMove(dx, dy) {
    ipcRenderer.send('facm-drag-move', { dx, dy });
  },
  dragEnd() {
    ipcRenderer.send('facm-drag-end');
  }
});
