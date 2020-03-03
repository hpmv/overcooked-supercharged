function setUpScheduleBackgroundClick(scheduleBackground, component) {
    var isWaiting = false;
    async function maybeCall(x, y) {
        // poor man's debounce.
        if (isWaiting) return;
        await component.invokeMethodAsync('HandleScheduleBackgroundClick', x, y);
        isWaiting = false;
    }

    scheduleBackground.addEventListener('mousedown', e => {
        e.preventDefault();
        let rect = scheduleBackground.getBoundingClientRect();
        let x = e.clientX - rect.left;
        let y = e.clientY - rect.top;
        maybeCall(x, y);

        var moveListener = e => {
            let rect = scheduleBackground.getBoundingClientRect();
            let x = e.clientX - rect.left;
            let y = e.clientY - rect.top;
            maybeCall(x, y);
        };

        document.addEventListener('mousemove', moveListener);

        var upListener = e => {
            document.removeEventListener('mousemove', moveListener);
            document.removeEventListener('mouseup', upListener);
        };

        document.addEventListener('mouseup', upListener);
    });
}

function setUpEntityRecordVisualizerHandlers(component, container) {
    container.addEventListener('mousedown', e => {
        let x = e.clientX;
        let y = e.clientY;

        let paths = [];
        for (let item of container.querySelectorAll('[data-entity-path]')) {
            let rect = item.getBoundingClientRect();
            if (x >= rect.left && x <= rect.right && y >= rect.top && y <= rect.bottom) {
                paths.push(item.getAttribute('data-entity-path'));
            }
        }
        let outerRect = container.getBoundingClientRect();

        component.invokeMethodAsync('HandleEntityClicks', paths, x - outerRect.left, y - outerRect.top);
    });
}