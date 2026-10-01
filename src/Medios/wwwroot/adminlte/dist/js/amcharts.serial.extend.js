AmCharts.addInitHandler(function (chart) {
    if (undefined === chart.autoGenerateGraf)// || !isObject(chart.autoGenerateGraf))
        chart.autoGenerateGraf = false;
    var version = chart.version.split('.');
    if ((Number(version[0]) < 3) || (3 === Number(version[0]) && (Number(version[1]) < 13)))
        return;
    if (chart.autoGenerateGraf)
    {
        chart.addListener('init', function (sender)
        {
            if(sender.chart.dataProvider.length !=0)
            sender.chart.graphs = [{
                "balloonText": "<b>[[title]]</b><br><span style='font-size:14px'>[[category]]: <b>[[value]]</b></span>",
                "fillAlphas": 0.8,
                "labelText": "[[value]]",
                "lineAlpha": 0.3,
                "title": "Sin Reclamo",
                "type": "column",
                "color": "#000000",
                "valueField": "Value"
            }, {
                "balloonText": "<b>[[title]]</b><br><span style='font-size:14px'>[[category]]: <b>[[value]]</b></span>",
                "fillAlphas": 0.8,
                "labelText": "[[value]]",
                "lineAlpha": 0.3,
                "title": "Nivel 1 IDA",
                "type": "column",
                "color": "#000000",
                "valueField": "Value2"
            }
            , {
                "balloonText": "<b>[[title]]</b><br><span style='font-size:14px'>[[category]]: <b>[[value]]</b></span>",
                "fillAlphas": 0.8,
                "labelText": "[[value]]",
                "lineAlpha": 0.3,
                "title": "Nivel 1 Vuelta",
                "type": "column",
                "color": "#000000",
                "valueField": "Value3"
            }
            , {
                "balloonText": "<b>[[title]]</b><br><span style='font-size:14px'>[[category]]: <b>[[value]]</b></span>",
                "fillAlphas": 0.8,
                "labelText": "[[value]]",
                "lineAlpha": 0.3,
                "title": "Nivel 2 IDA",
                "type": "column",
                "color": "#000000",
                "valueField": "Value4"
            }
            , {
                "balloonText": "<b>[[title]]</b><br><span style='font-size:14px'>[[category]]: <b>[[value]]</b></span>",
                "fillAlphas": 0.8,
                "labelText": "[[value]]",
                "lineAlpha": 0.3,
                "title": "Nivel 2 VUELTA",
                "type": "column",
                "color": "#000000",
                "valueField": "Value5"
            }
            , {
                "balloonText": "<b>[[title]]</b><br><span style='font-size:14px'>[[category]]: <b>[[value]]</b></span>",
                "fillAlphas": 0.8,
                "labelText": "[[value]]",
                "lineAlpha": 0.3,
                "title": "Incumplimiento IDA",
                "type": "column",
                "color": "#000000",
                "valueField": "Value6"
            }
            , {
                "balloonText": "<b>[[title]]</b><br><span style='font-size:14px'>[[category]]: <b>[[value]]</b></span>",
                "fillAlphas": 0.8,
                "labelText": "[[value]]",
                "lineAlpha": 0.3,
                "title": "Incumplimiento VUELTA",
                "type": "column",
                "color": "#000000",
                "valueField": "Value7"
            }]
        })
        function isObject(obj) {
            return 'object' === typeof (obj);
        }

    }
    
}, ['serial']);