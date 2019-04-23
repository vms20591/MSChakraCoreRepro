function asyncTask() {
    return new Promise((res, rej) => {
        res();
    });
}

asyncTask().then(() => {
    host.echo('done');
});