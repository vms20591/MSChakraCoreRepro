function asyncTask(ms) {
    return new Promise((res, rej) => {
        delay(ms || 1000);
        res();
    });
}

function delay(ms) {
    let last = (new Date()).getTime();
    while (true) {
        let now = (new Date()).getTime();
        if (now - last >= ms) {
            return;
        }
    }
}

var id = Math.random();

host.echo(id + ' started @ ' + new Date().toISOString());

asyncTask().then(() => {
    host.echo(id + ' done @ ' + new Date().toISOString());
});