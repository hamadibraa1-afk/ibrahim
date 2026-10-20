// Karma for unit tests. Uses CHROME_BIN when set (CI images and the dev container ship Chromium there).
module.exports = function (config) {
  config.set({
    basePath: '',
    frameworks: ['jasmine', '@angular-devkit/build-angular'],
    plugins: [
      require('karma-jasmine'),
      require('karma-chrome-launcher'),
      require('karma-jasmine-html-reporter'),
      require('karma-coverage'),
      require('@angular-devkit/build-angular/plugins/karma'),
    ],
    client: { jasmine: { random: true }, clearContext: false },
    coverageReporter: { dir: require('path').join(__dirname, './coverage/web-staff'), subdir: '.', reporters: [{ type: 'text-summary' }] },
    reporters: ['progress'],
    browsers: ['ChromeHeadlessCI'],
    customLaunchers: {
      ChromeHeadlessCI: { base: 'ChromeHeadless', flags: ['--no-sandbox', '--disable-gpu', '--disable-dev-shm-usage'] },
    },
    singleRun: true,
    restartOnFileChange: false,
  });
};
