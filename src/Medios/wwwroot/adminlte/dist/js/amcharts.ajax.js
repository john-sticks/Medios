AmCharts.translations.ajax = {};
AmCharts.addInitHandler(function (chart) {
    if (undefined === chart.ajax || !isObject(chart.ajax))
        chart.ajax = {};
    if (undefined === chart.autoGenerateGraphs)
        chart.autoGenerateGraphs = false;
    /**
     * Check charts version for compatibility:
     * the first compatible version is 3.13
     */
    var version = chart.version.split('.');
    if ((Number(version[0]) < 3) || (3 === Number(version[0]) && (Number(version[1]) < 13)))
        return;

    var defaults = {
        'url': '',
        'params': {},
        'type': 'POST',
        'chart': chart,
        'showCurtain': true
    };
    var l = chart.ajax;


    /**
  * Create a function that can be used to load data (or reload via API)
  */
    l.loadData = function (params) {

        /**
         * Load all files in a row
         */
        if ('stock' === chart.type) {

            return;

        } else {

            applyDefaults(l);

            if (l.url == "")
                return;
            if (undefined !== params)
                l.params = params;
            // preserve animation
            if (undefined !== chart.startDuration && (0 < chart.startDuration)) {
                l.startDuration = chart.startDuration;
                chart.startDuration = 0;
            }

            // set empty data set
            if (undefined === chart.dataProvider)
                chart.dataProvider = [];

            dataLoad(l.url, chart, l, 'dataProvider');

        }
    }
    l.loadData();
    /**
* Loads a file and determines correct parsing mechanism for it
*/
    function dataLoad(url, holder, options, providerKey) {

        // set default providerKey
        if (undefined === providerKey)
            providerKey = 'dataProvider';

        if (options.showCurtain)
        showCurtain(undefined, options.noStyles);



        // load the file
        AmCharts.loadData(url, options, function (response) {

            // error?
            if (false === response) {
                callFunction(options.error, options, chart);
                raiseError(AmCharts.__('Error loading data', chart.language) + ': ' + url, false, options);
            } else {
                rows = JSON.parse(response);
                holder[providerKey] = [];
                for (var i = 0; i < rows.length; i++) {
                    holder[providerKey].push(rows[i]);
                }
            }
            
            removeCurtain();
            if (holder.autoGenerateGraphs && holder.type == 'serial')
            {
                GenerateGraphs(holder);
            }
            holder.validateData();
        });

    }

    function GenerateGraphs(holder)
    {
        holder.graphs = [];
        
        if (holder.dataProvider.length != 0) {
            
                for (var name in holder.dataProvider[0])
                {
                    if(name != holder.categoryField)
                        holder.graphs.push(MakeGraphs(holder.dataProvider[0][name].Key, '[[\''+name+'\']].Value'));
                }
           
            //holder.graphs = 
        }
    }

    function MakeGraphs(title, valueField)
    {
        return {
            "balloonText": "<b>[[title]]</b><br><span style='font-size:14px'>[[category]]: <b>[[value]]</b></span>",
            "fillAlphas": 0.8,
            "labelText": "[[value]]",
            "lineAlpha": 0.3,
            "title": title,
            "type": "column",
            "color": "#000000",
            "valueField": valueField
        }
    }

    function applyDefaults(obj) {
        for (var x in defaults) {
            if (defaults.hasOwnProperty(x))
                setDefault(obj, x, defaults[x]);
        }
    }
    /**
   * Checks if object property is set, sets with a default if it isn't
   */
    function setDefault(obj, key, value) {
        if (undefined === obj[key])
            obj[key] = value;
    }
    function isObject(obj) {
        return 'object' === typeof (obj);
    }

    /**
     * Returns true is argument is a function
     */
    function isFunction(obj) {
        return 'function' === typeof (obj);
    }

    /**
   * Raises an internal error (writes it out to console)
   */
    function raiseError(msg, error, options) {

        if (options.showErrors)
            showCurtain(msg, options.noStyles);
        else {
            removeCurtain();
            console.log(msg);
        }

    }

    /**
     * Shows curtain over chart area
     */
    function showCurtain(msg, noStyles) {

        // remove previous curtain if there is one
        removeCurtain();

        // did we pass in the message?
        if (undefined === msg)
            msg = AmCharts.__('Loading data...', chart.language);

        // create and populate curtain element
        var curtain = document.createElement('div');
        curtain.setAttribute('id', chart.div.id + '-curtain');
        curtain.className = 'amcharts-dataloader-curtain';

        if (true !== noStyles) {
            curtain.style.position = 'absolute';
            curtain.style.top = 0;
            curtain.style.left = 0;
            curtain.style.width = (undefined !== chart.realWidth ? chart.realWidth : chart.divRealWidth) + 'px';
            curtain.style.height = (undefined !== chart.realHeight ? chart.realHeight : chart.divRealHeight) + 'px';
            curtain.style.textAlign = 'center';
            curtain.style.display = 'table';
            curtain.style.fontSize = '20px';
            try {
                curtain.style.background = 'rgba(255, 255, 255, 0.3)';
            }
            catch (e) {
                curtain.style.background = 'rgb(255, 255, 255)';
            }
            curtain.innerHTML = '<div style="display: table-cell; vertical-align: middle;">' + msg + '</div>';
        } else {
            curtain.innerHTML = msg;
        }
        chart.containerDiv.appendChild(curtain);

        l.curtain = curtain;
    }

    /**
     * Removes the curtain
     */
    function removeCurtain() {
        try {
            if (undefined !== l.curtain)
                chart.containerDiv.removeChild(l.curtain);
        } catch (e) {
            // do nothing
        }

        l.curtain = undefined;

    }

    function callFunction(func, param1, param2, param3) {
        if ('function' === typeof func)
            func.call(l, param1, param2, param3);
    }
}, ['pie', 'serial', 'xy', 'funnel', 'radar', 'gauge', 'gantt', 'stock']);

AmCharts.loadData = function (url, options, handler) {
    // create the request
    var request;
    if (window.XMLHttpRequest) {
        // IE7+, Firefox, Chrome, Opera, Safari
        request = new XMLHttpRequest();
    } else {
        // code for IE6, IE5
        request = new ActiveXObject('Microsoft.XMLHTTP');
    }

    // set handler for data if async loading
    request.onreadystatechange = function () {

        if (4 === request.readyState && 404 === request.status)
            handler.call(this, false);

        else if (4 === request.readyState && 200 === request.status)
            handler.call(this, request.responseText);

    };

    // load the file
    try {
        request.open('POST', url, false);
        request.setRequestHeader("Accept", "application/json, text/javascript, */*; q=0.01");
        request.setRequestHeader("Content-Type", "application/json; charset=utf-8");
        request.send(AmCharts.parseParams(options.params));
    } catch (e) {
        handler.call(this, false);
    }

};
AmCharts.parseParams = function (queryString) {
    var parametros = {}, queries, temp, i, l;

    // Split into key/value pairs
    queries = queryString.split("&");

    // Convert the array of strings into an object
    for (i = 0, l = queries.length; i < l; i++) {
        temp = queries[i].split('=');
        parametros[temp[0]] = temp[1];
    }
    return "{graficoParams:" + JSON.stringify(parametros) + "}";
};
if (undefined === AmCharts.__) {
    AmCharts.__ = function (msg, language) {
        if (undefined !== language && undefined !== AmCharts.translations.ajax[language] && undefined !== AmCharts.translations.ajax[language][msg])
            return AmCharts.translations.ajax[language][msg];
        else
            return msg;
    };
}


AmCharts.translations.ajax.es = {
    'Error loading data': 'Error al cargar los datos',
    'Loading data...': 'Cargando datos...'
}